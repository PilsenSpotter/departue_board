using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WF = System.Windows.Forms;
using DepartureBoard.Models;
using DepartureBoard.Services;

namespace DepartureBoard;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly GolemioClient _client = new();
    private readonly StopLookupService _stopLookup = new();
    private readonly OfflineCacheService _cache = new();
    private readonly DispatcherTimer _timer;
    private CancellationTokenSource? _stopSearchCts;
    private bool _isLoading;
    private bool _pendingFilterRefresh;
    private readonly ResourceDictionary _darkTheme;
    private readonly ResourceDictionary _lightTheme;
    private ResourceDictionary? _currentTheme;
    private bool _isLightTheme;
    private bool _isDisplayMode;
    private bool _isBoardMode;
    private bool _rememberSettings = true;
    private bool _isLoadingSettings;

    private readonly List<string> _selectedStopIds = new();
    private int _minutesAfter = 20;
    private int _refreshSeconds = 5;
    private string _apiKey = string.Empty;
    private string? _statusMessage;
    private string _stopSearchText = string.Empty;
    private bool _showBus = true;
    private bool _showTram = true;
    private bool _showMetro = true;
    private bool _showTrain = true;
    private bool _showTrolley = true;
    private bool _hasPlatformFilters;
    private bool _hasLineFilters;
    private AccessibilityFilter _accessibilityFilter = AccessibilityFilter.All;
    private bool _showOnTimeOnly;
    private string _newPresetName = string.Empty;
    private bool _alertsEnabled;
    private int _alertMinutesThreshold = 3;
    private int _alertDelayThreshold = 5;
    private readonly HashSet<string> _alertedDepartures = new(StringComparer.OrdinalIgnoreCase);
    private readonly WF.NotifyIcon _notifyIcon;
    private double _defaultListFontSize;
    private readonly double _displayModeFontSize = 16;
    private WindowState _normalWindowState;
    private WindowStyle _normalWindowStyle;
    private ResizeMode _normalResizeMode;
    private bool _isFullscreenActive;
    private readonly DispatcherTimer _clockTimer;
    private string _currentTimeDisplay = DateTime.Now.ToString("HH:mm:ss");

    public ObservableCollection<DepartureDisplay> Departures { get; } = new();
    public ObservableCollection<StopEntry> StopResults { get; } = new();
    public ObservableCollection<StopEntry> SelectedStops { get; } = new();
    public ObservableCollection<PlatformFilter> Platforms { get; } = new();
    public ObservableCollection<LineFilter> Lines { get; } = new();
    public ObservableCollection<Preset> Presets { get; } = new();

    public bool IsLightTheme
    {
        get => _isLightTheme;
        set
        {
            if (SetField(ref _isLightTheme, value))
            {
                ApplyTheme(value ? _lightTheme : _darkTheme);
                OnPropertyChanged(nameof(ThemeToggleLabel));
                SaveUserSettingsIfEnabled();
            }
        }
    }

    public string ThemeToggleLabel => IsLightTheme ? "Svetly motiv" : "Tmavy motiv";

    public bool IsDisplayMode
    {
        get => _isDisplayMode;
        set
        {
            if (SetField(ref _isDisplayMode, value))
            {
                ApplyDisplayMode(value);
                SaveUserSettingsIfEnabled();
            }
        }
    }

    public bool IsBoardMode
    {
        get => _isBoardMode;
        set
        {
            if (SetField(ref _isBoardMode, value))
            {
                ApplyBoardMode(value);
                SaveUserSettingsIfEnabled();
            }
        }
    }

    public bool RememberSettings
    {
        get => _rememberSettings;
        set
        {
            if (SetField(ref _rememberSettings, value))
            {
                if (!value)
                {
                    Presets.Clear();
                    _ = _cache.ClearUserSettingsAsync();
                }
                else
                {
                    SaveUserSettingsIfEnabled();
                }
            }
        }
    }

    public bool ShowBus
    {
        get => _showBus;
        set
        {
            if (SetField(ref _showBus, value))
            {
                TriggerFilterRefresh();
                SaveUserSettingsIfEnabled();
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
                SaveUserSettingsIfEnabled();
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
                SaveUserSettingsIfEnabled();
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
                SaveUserSettingsIfEnabled();
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
                SaveUserSettingsIfEnabled();
            }
        }
    }

    public bool ShowStopName => _selectedStopIds.Count > 1;
    public AccessibilityFilter AccessibilityFilter
    {
        get => _accessibilityFilter;
        set
        {
            if (SetField(ref _accessibilityFilter, value))
            {
                TriggerFilterRefresh();
                SaveUserSettingsIfEnabled();
            }
        }
    }
    public bool ShowOnTimeOnly
    {
        get => _showOnTimeOnly;
        set
        {
            if (SetField(ref _showOnTimeOnly, value))
            {
                TriggerFilterRefresh();
                SaveUserSettingsIfEnabled();
            }
        }
    }
    public bool HasPlatformFilters
    {
        get => _hasPlatformFilters;
        private set => SetField(ref _hasPlatformFilters, value);
    }
    public bool HasLineFilters
    {
        get => _hasLineFilters;
        private set => SetField(ref _hasLineFilters, value);
    }

    public string NewPresetName
    {
        get => _newPresetName;
        set => SetField(ref _newPresetName, value);
    }

    public bool AlertsEnabled
    {
        get => _alertsEnabled;
        set
        {
            if (SetField(ref _alertsEnabled, value))
            {
                if (!value)
                {
                    _alertedDepartures.Clear();
                }
                SaveUserSettingsIfEnabled();
            }
        }
    }

    public int AlertMinutesThreshold
    {
        get => _alertMinutesThreshold;
        set
        {
            if (value < 0) value = 0;
            if (SetField(ref _alertMinutesThreshold, value))
            {
                SaveUserSettingsIfEnabled();
            }
        }
    }

    public int AlertDelayThreshold
    {
        get => _alertDelayThreshold;
        set
        {
            if (value < 0) value = 0;
            if (SetField(ref _alertDelayThreshold, value))
            {
                SaveUserSettingsIfEnabled();
            }
        }
    }

    public int MinutesAfter
    {
        get => _minutesAfter;
        set
        {
            if (value <= 0) value = 1;
            if (SetField(ref _minutesAfter, value))
            {
                SaveUserSettingsIfEnabled();
            }
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
                SaveUserSettingsIfEnabled();
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

    public string CurrentTimeDisplay
    {
        get => _currentTimeDisplay;
        set => SetField(ref _currentTimeDisplay, value);
    }

    public IEnumerable<DepartureDisplay> BoardDepartures => Departures;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        _darkTheme = Resources.MergedDictionaries.FirstOrDefault() ?? new ResourceDictionary
        {
            Source = new Uri("Themes/DarkTheme.xaml", UriKind.Relative)
        };
        _lightTheme = new ResourceDictionary
        {
            Source = new Uri("Themes/LightTheme.xaml", UriKind.Relative)
        };
        _currentTheme = _darkTheme;
        if (!Resources.MergedDictionaries.Contains(_currentTheme))
        {
            Resources.MergedDictionaries.Insert(0, _currentTheme);
        }

        ApiKey = _client.ApiKey ?? string.Empty;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(_refreshSeconds)
        };
        _timer.Tick += async (_, _) => await RefreshDeparturesAsync();

        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += (_, _) => CurrentTimeDisplay = DateTime.Now.ToString("HH:mm:ss");

        _defaultListFontSize = DeparturesList.FontSize;
        _normalWindowState = WindowState;
        _normalWindowStyle = WindowStyle;
        _normalResizeMode = ResizeMode;

        _notifyIcon = new WF.NotifyIcon
        {
            Visible = true,
            Text = "PID Departure Board",
            Icon = LoadNotifyIcon()
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await LoadUserSettingsAsync();
        _ = RefreshDeparturesAsync();
        _timer.Start();
        _clockTimer.Start();
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape && (IsDisplayMode || IsBoardMode))
        {
            if (IsBoardMode)
            {
                IsBoardMode = false;
            }
            else
            {
                IsDisplayMode = false;
            }
            e.Handled = true;
        }
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
                ClearLineFilters();
                return;
            }

            StatusMessage = "Nacitam odjezdy...";

            var departures = await _client.GetDeparturesAsync(
                _selectedStopIds,
                ApiKey,
                MinutesAfter);

            UpdatePlatformFilters(departures);
            await _cache.SaveDeparturesAsync(_selectedStopIds, MinutesAfter, departures, CancellationToken.None);

            var tripIds = departures
                .Select(d => d.Trip?.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var vehicleInfos = tripIds.Count == 0
                ? (IReadOnlyDictionary<string, VehiclePositionInfo>)new Dictionary<string, VehiclePositionInfo>(StringComparer.OrdinalIgnoreCase)
                : await _client.GetVehicleInfoByTripAsync(tripIds, cancellationToken: CancellationToken.None);

            var now = DateTimeOffset.Now;
            var mapped = BuildDepartureDisplays(departures, vehicleInfos, now);

            Departures.Clear();
            foreach (var item in mapped)
            {
                Departures.Add(item);
            }

            UpdateLineFilters(departures);
            UpdatePlatformFilters(departures);
            CheckAlerts(mapped);

            OnPropertyChanged(nameof(BoardDepartures));
            StatusMessage = $"Posledni aktualizace: {DateTime.Now:HH:mm:ss}, odjezdu: {Departures.Count}.";
    }
    catch (Exception ex)
    {
        var cached = await _cache.LoadDeparturesAsync();
        if (cached != null && StopSetsCompatible(cached.StopIds, _selectedStopIds))
        {
                UpdateLineFilters(cached.Departures);
                UpdatePlatformFilters(cached.Departures);
                var mapped = BuildDepartureDisplays(cached.Departures, new Dictionary<string, VehiclePositionInfo>(), DateTimeOffset.Now);

                Departures.Clear();
                foreach (var item in mapped)
                {
                    Departures.Add(item);
                }

                CheckAlerts(mapped);
                OnPropertyChanged(nameof(BoardDepartures));
                StatusMessage = $"Offline rezim: ukazuji cache z {cached.SavedAt.LocalDateTime:HH:mm} (odjezdu: {Departures.Count}, okno {cached.MinutesAfter} min).";
            }
            else
            {
                StatusMessage = $"Nepodarilo se nacist data: {ex.Message}";
                Departures.Clear();
                OnPropertyChanged(nameof(BoardDepartures));
            }
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

    private List<DepartureDisplay> BuildDepartureDisplays(IEnumerable<Departure> departures, IReadOnlyDictionary<string, VehiclePositionInfo> vehicleInfos, DateTimeOffset now)
    {
        return departures
            .Where(IsModeAllowed)
            .Where(IsPlatformAllowed)
            .Where(IsLineAllowed)
            .Where(IsOnTimeAllowed)
            .Where(d => IsAccessibilityAllowed(d, vehicleInfos))
            .Select(d => MapDeparture(d, now, vehicleInfos))
            .Where(d => d is not null)
            .Cast<DepartureDisplay>()
            .OrderBy(d => d.When)
            .ToList();
    }

    private DepartureDisplay? MapDeparture(Departure departure, DateTimeOffset now, IReadOnlyDictionary<string, VehiclePositionInfo> vehicleInfos)
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
            countdown = ">1 min";
        }
        else
        {
            countdown = $"{Math.Round(diff.TotalMinutes)} min";
        }

        var (delayText, delayMinutes) = GetDelayInfo(departure);

        return new DepartureDisplay
        {
            Line = departure.Route?.ShortName ?? "-",
            Destination = departure.Trip?.Headsign ?? "-",
            StopName = ResolveStopName(departure),
            Platform = string.IsNullOrWhiteSpace(departure.Stop?.PlatformCode) ? "-" : departure.Stop!.PlatformCode!,
            DepartureTime = when.Value.ToLocalTime().ToString("HH:mm"),
            Countdown = countdown,
            Delay = delayText,
            Accessibility = GetAccessibilitySymbol(departure, vehicleInfos),
            VehicleType = ResolveVehicleLabel(departure, vehicleInfos),
            When = when.Value.ToLocalTime(),
            DelayMinutes = delayMinutes,
            DelayCategory = ResolveDelayCategory(delayMinutes)
        };
    }

    private (string text, double? minutes) GetDelayInfo(Departure departure)
    {
        var predicted = departure.DepartureTimestamp?.Predicted;
        var scheduled = departure.DepartureTimestamp?.Scheduled;

        if (predicted == null || scheduled == null)
        {
            return ("-", null);
        }

        var delay = predicted.Value - scheduled.Value;
        var minutes = delay.TotalMinutes;
        if (Math.Abs(minutes) < 0.5)
        {
            return ("vcas", minutes);
        }

        var sign = minutes >= 0 ? "+" : "-";
        return ($"{sign}{Math.Abs(minutes):0} min", minutes);
    }

    private string ResolveDelayCategory(double? minutes)
    {
        if (minutes is null)
        {
            return "none";
        }

        if (minutes >= 5)
        {
            return "major";
        }

        if (minutes >= 1)
        {
            return "minor";
        }

        return "none";
    }

    private bool IsOnTimeAllowed(Departure departure)
    {
        if (!ShowOnTimeOnly)
        {
            return true;
        }

        var (_, minutes) = GetDelayInfo(departure);
        return minutes.HasValue && Math.Abs(minutes.Value) < 0.5;
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

    private bool IsLineAllowed(Departure departure)
    {
        if (!HasLineFilters)
        {
            return true;
        }

        var line = departure.Route?.ShortName;
        if (string.IsNullOrWhiteSpace(line))
        {
            return true;
        }

        var allowed = Lines.Where(l => l.IsSelected).Select(l => l.Name);
        return allowed.Contains(line, StringComparer.OrdinalIgnoreCase);
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

    private void UpdateLineFilters(IEnumerable<Departure> departures)
    {
        var lines = departures
            .Select(d => d.Route?.ShortName?.Trim() ?? string.Empty)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var previousSelection = Lines.ToDictionary(l => l.Name, l => l.IsSelected, StringComparer.OrdinalIgnoreCase);

        ClearLineFilters();

        foreach (var line in lines)
        {
            var filter = new LineFilter
            {
                Name = line,
                IsSelected = previousSelection.TryGetValue(line, out var isSelected) ? isSelected : true
            };
            filter.PropertyChanged += LineFilterOnPropertyChanged;
            Lines.Add(filter);
        }

        HasLineFilters = Lines.Count > 0;
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

    private void ClearLineFilters()
    {
        foreach (var line in Lines)
        {
            line.PropertyChanged -= LineFilterOnPropertyChanged;
        }

        Lines.Clear();
        HasLineFilters = false;
    }

    private void PlatformFilterOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlatformFilter.IsSelected))
        {
            TriggerFilterRefresh();
        }
    }

    private void LineFilterOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LineFilter.IsSelected))
        {
            TriggerFilterRefresh();
        }
    }

    private bool IsAccessibilityAllowed(Departure departure, IReadOnlyDictionary<string, VehiclePositionInfo> vehicleInfos)
    {
        var accessible = IsAccessible(departure, vehicleInfos);
        return AccessibilityFilter switch
        {
            AccessibilityFilter.AccessibleOnly => accessible,
            AccessibilityFilter.HighFloorOnly => !accessible,
            _ => true
        };
    }

    private string ResolveVehicleLabel(Departure departure, IReadOnlyDictionary<string, VehiclePositionInfo> vehicleInfos)
    {
        if (vehicleInfos.Count == 0)
        {
            return ComposeVehicleLabel(departure, ResolveRouteTypeName(departure));
        }

        var tripId = departure.Trip?.Id;
        if (string.IsNullOrWhiteSpace(tripId))
        {
            return ComposeVehicleLabel(departure, ResolveRouteTypeName(departure));
        }

        if (vehicleInfos.TryGetValue(tripId, out var info))
        {
            if (!string.IsNullOrWhiteSpace(info?.DisplayName))
            {
                return ComposeVehicleLabel(departure, info.DisplayName!);
            }
        }

        return ComposeVehicleLabel(departure, ResolveRouteTypeName(departure));
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

    private string ComposeVehicleLabel(Departure departure, string name)
    {
        var emoji = ResolveVehicleEmoji(departure);
        if (string.IsNullOrWhiteSpace(emoji))
        {
            return name;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return emoji;
        }

        return $"{emoji} {name}";
    }

    private static string ResolveVehicleEmoji(Departure departure)
    {
        return departure.Route?.Type switch
        {
            0 => "🚋",   // tramvaj
            1 => "🚇",   // metro
            2 => "🚆",   // vlak
            3 => "🚌",   // bus
            11 => "🚎",  // trolejbus
            _ => string.Empty
        };
    }

    private string GetAccessibilitySymbol(Departure departure, IReadOnlyDictionary<string, VehiclePositionInfo> vehicleInfos)
    {
        var accessible = IsAccessible(departure, vehicleInfos);
        return accessible ? "♿" : string.Empty;
    }

    private bool IsAccessible(Departure departure, IReadOnlyDictionary<string, VehiclePositionInfo> vehicleInfos)
    {
        if (departure.Trip?.Id is string tripId &&
            vehicleInfos.TryGetValue(tripId, out var info) &&
            info?.WheelchairAccessible is bool vwFromVehicle)
        {
            return vwFromVehicle;
        }

        if (departure.Trip?.IsWheelchairAccessible is bool vwTrip)
        {
            return vwTrip;
        }

        bool accessible =
            (departure.Trip?.WheelchairAccessible ?? 0) == 1 ||
            (departure.WheelchairAccessible ?? 0) == 1 ||
            departure.Vehicle?.WheelchairAccessible == true ||
            departure.Vehicle?.LowFloor == true;

        return accessible;
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
        if (sender is not System.Windows.Controls.ListBox lb)
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
        OnPropertyChanged(nameof(BoardDepartures));
        SaveUserSettingsIfEnabled();
    }

    private void SavePresetButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!RememberSettings)
        {
            StatusMessage = "Ulozeni je vypnuto (povol Pamatovat nastaveni).";
            return;
        }

        var name = NewPresetName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = "Zadej jmeno setu.";
            return;
        }

        var existing = Presets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        var preset = existing ?? new Preset();

        preset.Name = name;
        preset.Stops = SelectedStops.ToList();
        preset.MinutesAfter = MinutesAfter;
        preset.RefreshSeconds = RefreshSeconds;
        preset.ShowBus = ShowBus;
        preset.ShowTram = ShowTram;
        preset.ShowMetro = ShowMetro;
        preset.ShowTrain = ShowTrain;
        preset.ShowTrolley = ShowTrolley;
        preset.AccessibilityFilter = AccessibilityFilter;
        preset.ShowOnTimeOnly = ShowOnTimeOnly;
        preset.AlertsEnabled = AlertsEnabled;
        preset.AlertMinutesThreshold = AlertMinutesThreshold;
        preset.AlertDelayThreshold = AlertDelayThreshold;

        if (existing == null)
        {
            Presets.Add(preset);
        }

        StatusMessage = $"Set \"{name}\" ulozen.";
        SaveUserSettingsIfEnabled();
    }

    private void ApplyPresetButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Preset preset })
        {
            return;
        }

        _isLoadingSettings = true;
        try
        {
            SelectedStops.Clear();
            foreach (var stop in preset.Stops ?? new List<StopEntry>())
            {
                SelectedStops.Add(stop);
            }
            UpdateSelectedStopIds();

            MinutesAfter = preset.MinutesAfter;
            RefreshSeconds = preset.RefreshSeconds;
            ShowBus = preset.ShowBus;
            ShowTram = preset.ShowTram;
            ShowMetro = preset.ShowMetro;
            ShowTrain = preset.ShowTrain;
            ShowTrolley = preset.ShowTrolley;
            AccessibilityFilter = preset.AccessibilityFilter;
            ShowOnTimeOnly = preset.ShowOnTimeOnly;
            AlertsEnabled = preset.AlertsEnabled;
            AlertMinutesThreshold = preset.AlertMinutesThreshold;
            AlertDelayThreshold = preset.AlertDelayThreshold;

            StatusMessage = $"Set \"{preset.Name}\" nahran.";
            _ = RefreshDeparturesAsync();
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    private void DeletePresetButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Preset preset })
        {
            return;
        }

        Presets.Remove(preset);
        StatusMessage = $"Set \"{preset.Name}\" odebran.";
        SaveUserSettingsIfEnabled();
    }

    private void CheckAlerts(IEnumerable<DepartureDisplay> departures)
    {
        if (!AlertsEnabled)
        {
            _alertedDepartures.Clear();
            return;
        }

        var now = DateTimeOffset.Now;
        var activeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var d in departures)
        {
            var key = $"{d.Line}|{d.Destination}|{d.Platform}|{d.DepartureTime}";
            activeKeys.Add(key);

            var minutesToDeparture = (d.When - now).TotalMinutes;
            var soon = minutesToDeparture >= 0 && minutesToDeparture <= AlertMinutesThreshold;
            var delayed = d.DelayMinutes.HasValue && d.DelayMinutes.Value >= AlertDelayThreshold;

            if ((soon || delayed) && !_alertedDepartures.Contains(key))
            {
                PlayAlertSound();
                StatusMessage = soon
                    ? $"Odjezd za {minutesToDeparture:0} min: {d.Line} {d.Destination} {d.Platform}"
                    : $"Zpozdeni {d.DelayMinutes:0} min: {d.Line} {d.Destination}";
                ShowNotification("Odjezdy", StatusMessage);
                _alertedDepartures.Add(key);
            }
        }

        _alertedDepartures.IntersectWith(activeKeys);
    }

    private static void PlayAlertSound()
    {
        try
        {
            SystemSounds.Exclamation.Play();
        }
        catch
        {
            // ignore sound errors
        }
    }

    private void ShowNotification(string title, string message)
    {
        try
        {
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = message;
            _notifyIcon.BalloonTipIcon = WF.ToolTipIcon.Info;
            if (_notifyIcon.Icon == null)
            {
                _notifyIcon.Icon = LoadNotifyIcon();
            }
            _notifyIcon.Visible = true;
            _notifyIcon.ShowBalloonTip(3000);
        }
        catch
        {
            // ignore notification errors
        }
    }

    private static System.Drawing.Icon LoadNotifyIcon()
    {
        try
        {
            var resourceUri = new Uri("pack://application:,,,/icon.ico", UriKind.Absolute);
            using var iconStream = System.Windows.Application.GetResourceStream(resourceUri)?.Stream;
            if (iconStream != null)
            {
                return new System.Drawing.Icon(iconStream);
            }
        }
        catch
        {
            // ignore resource load errors
        }

        try
        {
            var exePath = typeof(MainWindow).Assembly.Location;
            var associatedIcon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (associatedIcon != null)
            {
                return associatedIcon;
            }
        }
        catch
        {
            // ignore icon extraction errors
        }

        return System.Drawing.SystemIcons.Information;
    }

    protected override void OnClosed(EventArgs e)
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        base.OnClosed(e);
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

    private static bool StopSetsCompatible(IEnumerable<string> cached, IEnumerable<string> current)
    {
        var cachedSet = new HashSet<string>(cached ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var currentSet = new HashSet<string>(current ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        if (cachedSet.Count == 0 || currentSet.Count == 0)
        {
            return false;
        }

        return cachedSet.IsSupersetOf(currentSet) || cachedSet.SetEquals(currentSet);
    }

    private void TriggerFilterRefresh()
    {
        _pendingFilterRefresh = true;
        _ = RefreshDeparturesAsync();
    }

    private async Task LoadUserSettingsAsync()
    {
        try
        {
            _isLoadingSettings = true;
            var settings = await _cache.LoadUserSettingsAsync();
            if (settings == null)
            {
                return;
            }

            _rememberSettings = settings.RememberSettings;
            OnPropertyChanged(nameof(RememberSettings));

            IsLightTheme = settings.IsLightTheme;
            ShowBus = settings.ShowBus;
            ShowTram = settings.ShowTram;
            ShowMetro = settings.ShowMetro;
            ShowTrain = settings.ShowTrain;
            ShowTrolley = settings.ShowTrolley;
            AccessibilityFilter = settings.AccessibilityFilter;
            ShowOnTimeOnly = settings.ShowOnTimeOnly;
            AlertsEnabled = settings.AlertsEnabled;
            AlertMinutesThreshold = settings.AlertMinutesThreshold;
            AlertDelayThreshold = settings.AlertDelayThreshold;

            Presets.Clear();
            foreach (var preset in settings.Presets ?? new List<Preset>())
            {
                Presets.Add(preset);
            }

            MinutesAfter = settings.MinutesAfter <= 0 ? 20 : settings.MinutesAfter;
            RefreshSeconds = settings.RefreshSeconds < 5 ? 5 : settings.RefreshSeconds;

            SelectedStops.Clear();
            foreach (var stop in settings.SelectedStops ?? Enumerable.Empty<StopEntry>())
            {
                SelectedStops.Add(stop);
            }
            UpdateSelectedStopIds();

            if (settings.IsDisplayMode)
            {
                IsDisplayMode = true;
            }

            if (settings.IsBoardMode)
            {
                IsBoardMode = true;
            }
        }
        catch
        {
            // ignore settings load errors
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    private void SaveUserSettingsIfEnabled()
    {
        if (!_rememberSettings || _isLoadingSettings)
        {
            return;
        }

        try
        {
            var settings = new UserSettings
            {
                RememberSettings = _rememberSettings,
                SelectedStops = SelectedStops.ToList(),
                MinutesAfter = MinutesAfter,
                RefreshSeconds = RefreshSeconds,
                IsLightTheme = IsLightTheme,
                ShowBus = ShowBus,
                ShowTram = ShowTram,
                ShowMetro = ShowMetro,
                ShowTrain = ShowTrain,
                ShowTrolley = ShowTrolley,
                AccessibilityFilter = AccessibilityFilter,
                ShowOnTimeOnly = ShowOnTimeOnly,
                IsDisplayMode = IsDisplayMode,
                IsBoardMode = IsBoardMode,
                Presets = Presets.ToList(),
                AlertsEnabled = AlertsEnabled,
                AlertMinutesThreshold = AlertMinutesThreshold,
                AlertDelayThreshold = AlertDelayThreshold
            };

            _ = _cache.SaveUserSettingsAsync(settings);
        }
        catch
        {
            // ignore persistence errors
        }
    }

    private void ApplyBoardMode(bool enabled)
    {
        ApplyDisplayMode(_isDisplayMode);
    }

    private void ApplyDisplayMode(bool enabled)
    {
        if (HeaderPanel == null || ControlsPanel == null || DeparturesList == null)
        {
            return;
        }

        var shouldFullscreen = enabled || _isBoardMode;

        HeaderPanel.Visibility = shouldFullscreen ? Visibility.Collapsed : Visibility.Visible;
        ControlsPanel.Visibility = shouldFullscreen ? Visibility.Collapsed : Visibility.Visible;

        if (BottomPanel != null)
        {
            BottomPanel.Visibility = _isBoardMode ? Visibility.Collapsed : Visibility.Visible;
        }

        if (shouldFullscreen && !_isFullscreenActive)
        {
            _normalWindowState = WindowState;
            _normalWindowStyle = WindowStyle;
            _normalResizeMode = ResizeMode;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            Topmost = true;
            DeparturesList.FontSize = _displayModeFontSize;
            _isFullscreenActive = true;
        }
        else if (!shouldFullscreen && _isFullscreenActive)
        {
            WindowStyle = _normalWindowStyle;
            ResizeMode = _normalResizeMode;
            WindowState = _normalWindowState;
            Topmost = false;
            DeparturesList.FontSize = _defaultListFontSize;
            _isFullscreenActive = false;
        }
    }

    private void ApplyTheme(ResourceDictionary theme)
    {
        if (theme == _currentTheme)
        {
            return;
        }

        if (_currentTheme != null)
        {
            Resources.MergedDictionaries.Remove(_currentTheme);
        }

        Resources.MergedDictionaries.Insert(0, theme);
        _currentTheme = theme;
    }
}
