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
    private bool _pendingFilterRefresh;

    private readonly List<string> _selectedStopIds = new();
    private int _minutesAfter = 20;
    private int _refreshSeconds = 30;
    private string _apiKey = string.Empty;
    private string? _statusMessage;
    private string _stopSearchText = string.Empty;
    private bool _showBus = true;
    private bool _showTram = true;
    private bool _showMetro = true;
    private bool _showTrain = true;
    private bool _showTrolley = true;
    private bool _hasPlatformFilters;

    public ObservableCollection<DepartureDisplay> Departures { get; } = new();
    public ObservableCollection<StopEntry> StopResults { get; } = new();
    public ObservableCollection<StopEntry> SelectedStops { get; } = new();
    public ObservableCollection<PlatformFilter> Platforms { get; } = new();

    public bool ShowBus
    {
        get => _showBus;
        set
        {
            if (SetField(ref _showBus, value))
            {
                TriggerFilterRefresh();
            }
        }
    }

    public bool ShowTram
    {
        get => _showTram;
        set
        {
            if (SetField(ref _showTram, value))
            {
                TriggerFilterRefresh();
            }
        }
    }

    public bool ShowMetro
    {
        get => _showMetro;
        set
        {
            if (SetField(ref _showMetro, value))
            {
                TriggerFilterRefresh();
            }
        }
    }

    public bool ShowTrain
    {
        get => _showTrain;
        set
        {
            if (SetField(ref _showTrain, value))
            {
                TriggerFilterRefresh();
            }
        }
    }

    public bool ShowTrolley
    {
        get => _showTrolley;
        set
        {
            if (SetField(ref _showTrolley, value))
            {
                TriggerFilterRefresh();
            }
        }
    }

    public bool ShowStopName => _selectedStopIds.Count > 1;
    public bool HasPlatformFilters
    {
        get => _hasPlatformFilters;
        private set => SetField(ref _hasPlatformFilters, value);
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
            _pendingFilterRefresh = true;
            return;
        }

        _isLoading = true;
        _pendingFilterRefresh = false;

        try
        {
            if (_selectedStopIds.Count == 0)
            {
                StatusMessage = "Vyber zastavku.";
                ClearPlatformFilters();
                return;
            }

            StatusMessage = "Nacitam odjezdy...";

            var departures = await _client.GetDeparturesAsync(
                _selectedStopIds,
                ApiKey,
                MinutesAfter);

            UpdatePlatformFilters(departures);

            var tripIds = departures
                .Select(d => d.Trip?.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var vehicleTypes = tripIds.Count == 0
                ? (IReadOnlyDictionary<string, VehiclePositionInfo>)new Dictionary<string, VehiclePositionInfo>(StringComparer.OrdinalIgnoreCase)
                : await _client.GetVehicleInfoByTripAsync(tripIds, cancellationToken: CancellationToken.None);

            var now = DateTimeOffset.Now;
            var mapped = departures
                .Where(IsModeAllowed)
                .Where(IsPlatformAllowed)
                .Select(d => MapDeparture(d, now, vehicleTypes))
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
            if (_pendingFilterRefresh)
            {
                _pendingFilterRefresh = false;
                await RefreshDeparturesAsync();
            }
        }
    }

    private DepartureDisplay? MapDeparture(Departure departure, DateTimeOffset now, IReadOnlyDictionary<string, VehiclePositionInfo> vehicleTypes)
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
            StopName = ResolveStopName(departure),
            Platform = string.IsNullOrWhiteSpace(departure.Stop?.PlatformCode) ? "-" : departure.Stop!.PlatformCode!,
            DepartureTime = when.Value.ToLocalTime().ToString("HH:mm"),
            Countdown = countdown,
            Delay = GetDelayText(departure),
            Accessibility = GetAccessibilitySymbol(departure, vehicleTypes),
            VehicleType = ResolveVehicleType(departure, vehicleTypes),
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

    private static bool IsMhdMode(Departure departure)
    {
        return departure.Route?.Type is 0 or 1 or 3 or 11;
    }

    private bool IsModeAllowed(Departure departure)
    {
        var type = departure.Route?.Type;
        return type switch
        {
            0 => ShowTram,   // Tramvaj
            1 => ShowMetro,  // Metro
            2 => ShowTrain,  // Vlak
            3 => ShowBus,    // Bus
            11 => ShowTrolley, // Trolejbus
            _ => true // neznamy -> zobrazit
        };
    }

    private bool IsPlatformAllowed(Departure departure)
    {
        if (!IsMhdMode(departure))
        {
            return true;
        }

        var platform = departure.Stop?.PlatformCode;
        if (string.IsNullOrWhiteSpace(platform))
        {
            return true;
        }

        if (!HasPlatformFilters)
        {
            return true;
        }

        var allowed = Platforms.Where(p => p.IsSelected).Select(p => p.Name);
        return allowed.Contains(platform, StringComparer.OrdinalIgnoreCase);
    }

    private void UpdatePlatformFilters(IEnumerable<Departure> departures)
    {
        var platforms = departures
            .Where(IsMhdMode)
            .Select(d => d.Stop?.PlatformCode?.Trim() ?? string.Empty)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p)
            .ToList();

        var previousSelection = Platforms.ToDictionary(p => p.Name, p => p.IsSelected, StringComparer.OrdinalIgnoreCase);

        ClearPlatformFilters();

        foreach (var platformName in platforms)
        {
            var filter = new PlatformFilter
            {
                Name = platformName,
                IsSelected = previousSelection.TryGetValue(platformName, out var isSelected) ? isSelected : true
            };
            filter.PropertyChanged += PlatformFilterOnPropertyChanged;
            Platforms.Add(filter);
        }

        HasPlatformFilters = Platforms.Count > 0;
    }

    private void ClearPlatformFilters()
    {
        foreach (var platform in Platforms)
        {
            platform.PropertyChanged -= PlatformFilterOnPropertyChanged;
        }

        Platforms.Clear();
        HasPlatformFilters = false;
    }

    private void PlatformFilterOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlatformFilter.IsSelected))
        {
            TriggerFilterRefresh();
        }
    }

    private string ResolveVehicleType(Departure departure, IReadOnlyDictionary<string, VehiclePositionInfo> vehicleTypes)
    {
        if (vehicleTypes.Count == 0)
        {
            return ResolveRouteTypeName(departure);
        }

        var tripId = departure.Trip?.Id;
        if (string.IsNullOrWhiteSpace(tripId))
        {
            return ResolveRouteTypeName(departure);
        }

        if (vehicleTypes.TryGetValue(tripId, out var info))
        {
            if (!string.IsNullOrWhiteSpace(info?.DisplayName))
            {
                return info.DisplayName!;
            }
        }

        return ResolveRouteTypeName(departure);
    }

    private static string ResolveRouteTypeName(Departure departure)
    {
        return departure.Route?.Type switch
        {
            0 => "tramvaj",
            1 => "metro",
            2 => "vlak",
            3 => "bus",
            11 => "trolejbus",
            _ => string.Empty
        };
    }

    private string GetAccessibilitySymbol(Departure departure, IReadOnlyDictionary<string, VehiclePositionInfo> vehicleInfos)
    {
        if (departure.Trip?.Id is string tripId &&
            vehicleInfos.TryGetValue(tripId, out var info) &&
            info?.WheelchairAccessible is bool vwFromVehicle)
        {
            return vwFromVehicle ? "♿" : string.Empty;
        }

        if (departure.Trip?.IsWheelchairAccessible is bool vwTrip)
        {
            return vwTrip ? "♿" : string.Empty;
        }

        bool accessible =
            (departure.Trip?.WheelchairAccessible ?? 0) == 1 ||
            (departure.WheelchairAccessible ?? 0) == 1 ||
            departure.Vehicle?.WheelchairAccessible == true ||
            departure.Vehicle?.LowFloor == true;

        return accessible ? "♿" : string.Empty;
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
        if (sender is not ListBox lb)
        {
            return;
        }

        foreach (var stop in e.AddedItems.OfType<StopEntry>())
        {
            if (SelectedStops.Any(s => s.PrimaryId.Equals(stop.PrimaryId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            SelectedStops.Add(stop);
        }

        lb.SelectedItem = null;

        UpdateSelectedStopIds();
        _ = RefreshDeparturesAsync();
    }

    private void RemoveSelectedStop_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: StopEntry stop })
        {
            return;
        }

        SelectedStops.Remove(stop);
        UpdateSelectedStopIds();
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

    private void UpdateSelectedStopIds()
    {
        _selectedStopIds.Clear();
        var ids = SelectedStops
            .SelectMany(s => s.StopIds)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        _selectedStopIds.AddRange(ids);

        OnPropertyChanged(nameof(ShowStopName));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private string ResolveStopName(Departure departure)
    {
        var apiName = departure.Stop?.Name;
        if (!string.IsNullOrWhiteSpace(apiName))
        {
            return apiName;
        }

        var stopId = departure.Stop?.Id;
        if (!string.IsNullOrWhiteSpace(stopId))
        {
            var match = SelectedStops.FirstOrDefault(s => s.StopIds.Contains(stopId, StringComparer.OrdinalIgnoreCase));
            if (match != null)
            {
                return match.Name;
            }

            return stopId;
        }

        return string.Empty;
    }

    private void TriggerFilterRefresh()
    {
        _pendingFilterRefresh = true;
        _ = RefreshDeparturesAsync();
    }
}
