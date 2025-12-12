using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DepartureBoard.Models;
using DepartureBoard.Services;

namespace DepartureBoard;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly GolemioClient _client = new();
    private readonly StopLookupService _stopLookup = new();
    private readonly DispatcherTimer _timer;
    private CancellationTokenSource? _stopSearchCts;
    private bool _isLoading;

    private readonly List<string> _selectedStopIds = new();
    private int _minutesAfter = 20;
    private int _refreshSeconds = 30;
    private string _apiKey = string.Empty;
    private string? _statusMessage;
    private string _stopSearchText = string.Empty;

    public ObservableCollection<DepartureDisplay> Departures { get; } = new();
    public ObservableCollection<StopEntry> StopResults { get; } = new();

    public bool ShowBus { get; set; } = true;
    public bool ShowTram { get; set; } = true;
    public bool ShowMetro { get; set; } = true;
    public bool ShowTrolley { get; set; } = true;

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
            if (SetField(ref _refreshSeconds, value))
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

            if (_selectedStopIds.Count == 0)
            {
                StatusMessage = "Vyber zastavku.";
                return;
            }

            StatusMessage = "Nacitam odjezdy...";

            var departures = await _client.GetDeparturesAsync(
                _selectedStopIds,
                ApiKey,
                MinutesAfter);

            var now = DateTimeOffset.Now;
            var mapped = departures
                .Where(IsModeAllowed)
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

            StatusMessage = $"Posledni aktualizace: {DateTime.Now:HH:mm:ss}, odjezdu: {Departures.Count}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Nepodarilo se nacist data: {ex.Message}";
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
            countdown = "za chvili";
        }
        else
        {
            countdown = $"{Math.Round(diff.TotalMinutes)} min";
        }

        return new DepartureDisplay
        {
            Line = departure.Route?.ShortName ?? "-",
            Destination = departure.Trip?.Headsign ?? "-",
            StopName = departure.Stop?.Name ?? string.Empty,
            Platform = string.IsNullOrWhiteSpace(departure.Stop?.PlatformCode) ? "-" : departure.Stop!.PlatformCode!,
            DepartureTime = when.Value.ToLocalTime().ToString("HH:mm"),
            Countdown = countdown,
            Delay = GetDelayText(departure),
            Accessibility = GetAccessibilitySymbol(departure),
            When = when.Value.ToLocalTime()
        };
    }

    private string GetDelayText(Departure departure)
    {
        var predicted = departure.DepartureTimestamp?.Predicted;
        var scheduled = departure.DepartureTimestamp?.Scheduled;

        if (predicted == null || scheduled == null)
        {
            return "-";
        }

        var delay = predicted.Value - scheduled.Value;
        if (Math.Abs(delay.TotalMinutes) < 0.5)
        {
            return "vcas";
        }

        var sign = delay.TotalMinutes >= 0 ? "+" : "-";
        return $"{sign}{Math.Abs(delay.TotalMinutes):0} min";
    }

    private bool IsModeAllowed(Departure departure)
    {
        var type = departure.Route?.Type;
        return type switch
        {
            0 => ShowTram,   // Tramvaj
            1 => ShowMetro,  // Metro
            2 => false,      // Vlak (skrýváme)
            3 => ShowBus,    // Bus
            11 => ShowTrolley, // Trolejbus
            _ => true // neznámé -> zobrazit
        };
    }

    private string GetAccessibilitySymbol(Departure departure)
    {
        bool accessible =
            (departure.Trip?.WheelchairAccessible ?? 0) == 1 ||
            (departure.WheelchairAccessible ?? 0) == 1 ||
            departure.Vehicle?.WheelchairAccessible == true ||
            departure.Vehicle?.LowFloor == true;

        return accessible ? "♿" : string.Empty;
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
            StatusMessage = _stopLookup.IsLoaded ? "Hledam zastavky..." : "Stahuji seznam zastavek PID...";
            var results = await _stopLookup.SearchAsync(query, 25, cts.Token);

            StopResults.Clear();
            foreach (var r in results)
            {
                StopResults.Add(r);
            }

            StatusMessage = StopResults.Count == 0
                ? "Zadna zastavka nenalezena."
                : $"Vyber zastavku ({StopResults.Count} navrhu).";
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
        catch (Exception ex)
        {
            StatusMessage = $"Vyhledani selhalo: {ex.Message}";
        }
    }

    private void StopResults_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox lb || lb.SelectedItem is not StopEntry stop)
        {
            return;
        }

        _selectedStopIds.Clear();
        _selectedStopIds.AddRange(stop.StopIds);

        StopSearchText = stop.Name;
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
}
