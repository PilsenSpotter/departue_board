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
using System.Windows.Input;
using System.Windows.Threading;
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
    private AccessibilityFilter _accessibilityFilter = AccessibilityFilter.All;
    private bool _showOnTimeOnly;
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
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await LoadUserSettingsAsync();
        _ = RefreshDeparturesAsync();
        _timer.Start();
        _clockTimer.Start();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
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

            OnPropertyChanged(nameof(BoardDepartures));
            StatusMessage = $"Posledni aktualizace: {DateTime.Now:HH:mm:ss}, odjezdu: {Departures.Count}.";
        }
        catch (Exception ex)
        {
            var cached = await _cache.LoadDeparturesAsync();
            if (cached != null && StopSetsCompatible(cached.StopIds, _selectedStopIds))
            {
                UpdatePlatformFilters(cached.Departures);
                var mapped = BuildDepartureDisplays(cached.Departures, new Dictionary<string, VehiclePositionInfo>(), DateTimeOffset.Now);

                Departures.Clear();
                foreach (var item in mapped)
                {
                    Departures.Add(item);
                }

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
        OnPropertyChanged(nameof(BoardDepartures));
        SaveUserSettingsIfEnabled();
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
                IsBoardMode = IsBoardMode
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
