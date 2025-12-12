using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DepartureBoard.Models;
using DepartureBoard.Services;
using System.Threading;

namespace DepartureBoard;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly GolemioClient _client = new();
    private readonly StopLookupService _stopLookup = new();
    private readonly DispatcherTimer _timer;
    private CancellationTokenSource? _stopSearchCts;
    private bool _isLoading;

    private string _stopId = string.Empty;
    private int _minutesAfter = 20;
    private int _refreshSeconds = 30;
    private string _apiKey = string.Empty;
    private string? _statusMessage;
    private string _stopSearchText = string.Empty;
    private string _selectedStopName = string.Empty;

    public ObservableCollection<DepartureDisplay> Departures { get; } = new();
    public ObservableCollection<StopEntry> StopResults { get; } = new();

    public string StopId
    {
        get => _stopId;
        set
        {
            if (SetField(ref _stopId, value))
            {
                OnPropertyChanged(nameof(SelectedStopLabel));
            }
        }
    }

    public int MinutesAfter
    {
        get => _minutesAfter;
        set
        {
            if (value <= 0) value = 1;
            SetField(ref _minutesAfter, value);
        }
    }

    public int RefreshSeconds
    {
        get => _refreshSeconds;
        set
        {
            if (value < 5) value = 5;
            if (SetField(ref _refreshSeconds, value) && _timer != null)
            {
                _timer.Interval = TimeSpan.FromSeconds(_refreshSeconds);
            }
        }
    }

    public string ApiKey
    {
        get => _apiKey;
        set => SetField(ref _apiKey, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public string StopSearchText
    {
        get => _stopSearchText;
        set => SetField(ref _stopSearchText, value);
    }

    public string SelectedStopName
    {
        get => _selectedStopName;
        set
        {
            if (SetField(ref _selectedStopName, value))
            {
                OnPropertyChanged(nameof(SelectedStopLabel));
            }
        }
    }

    public string SelectedStopLabel => string.IsNullOrWhiteSpace(StopId)
        ? "Nevybrano"
        : StopId;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        ApiKey = _client.ApiKey ?? string.Empty;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(_refreshSeconds)
        };
        _timer.Tick += async (_, _) => await RefreshDeparturesAsync();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _ = RefreshDeparturesAsync();
        _timer.Start();
    }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RefreshDeparturesAsync();
    }

    private async Task RefreshDeparturesAsync()
    {
        if (_isLoading)
        {
            return;
        }

        try
        {
            _isLoading = true;
            StatusMessage = "Načítám odjezdy...";

            var departures = await _client.GetDeparturesAsync(
                StopId.Trim(),
                ApiKey,
                MinutesAfter);

            var now = DateTimeOffset.Now;
            var mapped = departures
                .Select(d => MapDeparture(d, now))
                .Where(d => d is not null)
                .Cast<DepartureDisplay>()
                .OrderBy(d => d.When)
                .ToList();

            Departures.Clear();
            foreach (var item in mapped)
            {
                Departures.Add(item);
            }

            StatusMessage = $"Poslední aktualizace: {DateTime.Now:HH:mm:ss}, odjezdů: {Departures.Count}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Nepodařilo se načíst data: {ex.Message}";
        }
        finally
        {
            _isLoading = false;
        }
    }

    private DepartureDisplay? MapDeparture(Departure departure, DateTimeOffset now)
    {
        var when = departure.DepartureTimestamp?.Effective;
        if (when == null)
        {
            return null;
        }

        var diff = when.Value - now;
        string countdown;
        if (diff.TotalSeconds < -30)
        {
            countdown = "odjel";
        }
        else if (diff.TotalMinutes < 1)
        {
            countdown = "za chvíli";
        }
        else
        {
            countdown = $"{Math.Round(diff.TotalMinutes)} min";
        }

        return new DepartureDisplay
        {
            Line = departure.Route?.ShortName ?? "—",
            Destination = departure.Trip?.Headsign ?? "—",
            StopName = departure.Stop?.Name ?? string.Empty,
            Platform = string.IsNullOrWhiteSpace(departure.Stop?.PlatformCode) ? "—" : departure.Stop!.PlatformCode!,
            DepartureTime = when.Value.ToLocalTime().ToString("HH:mm"),
            Countdown = countdown,
            Delay = GetDelayText(departure),
            When = when.Value.ToLocalTime()
        };
    }

    private string GetDelayText(Departure departure)
    {
        var predicted = departure.DepartureTimestamp?.Predicted;
        var scheduled = departure.DepartureTimestamp?.Scheduled;

        if (predicted == null || scheduled == null)
        {
            return "—";
        }

        var delay = predicted.Value - scheduled.Value;
        if (Math.Abs(delay.TotalMinutes) < 0.5)
        {
            return "včas";
        }

        var sign = delay.TotalMinutes >= 0 ? "+" : "-";
        return $"{sign}{Math.Abs(delay.TotalMinutes):0} min";
    }

    private async void StopSearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        _stopSearchCts?.Cancel();

        var query = StopSearchText;
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
        {
            StopResults.Clear();
            return;
        }

        var cts = new CancellationTokenSource();
        _stopSearchCts = cts;

        try
        {
            StatusMessage = _stopLookup.IsLoaded ? "Hledám zastávky..." : "Stahuji seznam zastávek PID...";
            var results = await _stopLookup.SearchAsync(query, 25, cts.Token);

            StopResults.Clear();
            foreach (var r in results)
            {
                StopResults.Add(r);
            }

            StatusMessage = StopResults.Count == 0
                ? "Žádná zastávka nenalezena."
                : $"Vyber zastávku ({StopResults.Count} návrhů).";
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            StatusMessage = $"Vyhledání selhalo: {ex.Message}";
        }
    }

    private void StopResults_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox lb || lb.SelectedItem is not StopEntry stop)
        {
            return;
        }

        StopId = stop.Id;
        SelectedStopName = stop.Name;
                StopSearchText = stop.Id;
        StopResults.Clear();
        lb.SelectedItem = null;

        _ = RefreshDeparturesAsync();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

