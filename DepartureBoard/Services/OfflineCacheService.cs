using System.IO;
using System.Text.Json;
using DepartureBoard.Models;

namespace DepartureBoard.Services;

public class OfflineCacheService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _root;
    private readonly string _stopsPath;
    private readonly string _departuresPath;
    private readonly string _settingsPath;

    public OfflineCacheService()
    {
        _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DepartureBoard");
        _stopsPath = Path.Combine(_root, "stops.json");
        _departuresPath = Path.Combine(_root, "departures.json");
        _settingsPath = Path.Combine(_root, "settings.json");
    }

    public async Task SaveStopsAsync(IEnumerable<StopEntry> stops, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_root);
        var payload = new CachedStops
        {
            SavedAt = DateTimeOffset.UtcNow,
            Stops = stops.ToList()
        };

        await using var stream = File.Create(_stopsPath);
        await JsonSerializer.SerializeAsync(stream, payload, _jsonOptions, cancellationToken);
    }

    public async Task<CachedStops?> LoadStopsAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_stopsPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(_stopsPath);
        return await JsonSerializer.DeserializeAsync<CachedStops>(stream, _jsonOptions, cancellationToken);
    }

    public async Task SaveDeparturesAsync(IEnumerable<string> stopIds, int minutesAfter, IEnumerable<Departure> departures, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_root);
        var payload = new CachedDepartures
        {
            SavedAt = DateTimeOffset.UtcNow,
            StopIds = stopIds?.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>(),
            MinutesAfter = minutesAfter,
            Departures = departures.ToList()
        };

        await using var stream = File.Create(_departuresPath);
        await JsonSerializer.SerializeAsync(stream, payload, _jsonOptions, cancellationToken);
    }

    public async Task<CachedDepartures?> LoadDeparturesAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_departuresPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(_departuresPath);
        return await JsonSerializer.DeserializeAsync<CachedDepartures>(stream, _jsonOptions, cancellationToken);
    }

    public async Task SaveUserSettingsAsync(UserSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_root);
        await using var stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, _jsonOptions, cancellationToken);
    }

    public async Task<UserSettings?> LoadUserSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(_settingsPath);
        return await JsonSerializer.DeserializeAsync<UserSettings>(stream, _jsonOptions, cancellationToken);
    }

    public Task ClearUserSettingsAsync()
    {
        if (File.Exists(_settingsPath))
        {
            File.Delete(_settingsPath);
        }

        return Task.CompletedTask;
    }
}

public class CachedStops
{
    public DateTimeOffset SavedAt { get; set; }
    public List<StopEntry> Stops { get; set; } = new();
}

public class CachedDepartures
{
    public DateTimeOffset SavedAt { get; set; }
    public List<string> StopIds { get; set; } = new();
    public int MinutesAfter { get; set; }
    public List<Departure> Departures { get; set; } = new();
}
