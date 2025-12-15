using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using DepartureBoard.Models;
using Microsoft.VisualBasic.FileIO;

namespace DepartureBoard.Services;

public class StopLookupService
{
    private const string GtfsUrl = "https://data.pid.cz/PID_GTFS.zip";
    private static readonly TimeSpan StopsCacheMaxAge = TimeSpan.FromDays(7);
    private static readonly TimeSpan StopsRefreshInterval = TimeSpan.FromHours(12);

    private readonly HttpClient _httpClient = new();
    private readonly List<StopEntry> _stops = new();
    private readonly OfflineCacheService _cache = new();
    private Task? _loadTask;
    public bool IsLoaded { get; private set; }
    private DateTimeOffset? _lastDownloadUtc;

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        var needsRefresh = ShouldRefreshStops();
        if (IsLoaded && !needsRefresh)
        {
            return;
        }

        if (_loadTask == null || _loadTask.IsCanceled || _loadTask.IsFaulted || needsRefresh)
        {
            // Pouzijeme CancellationToken.None, aby se nacteni GTFS neukoncilo pri psani do textboxu.
            _loadTask = LoadStopsAsync(CancellationToken.None);
        }

        await _loadTask.ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StopEntry>> SearchAsync(string query, int limit = 20, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(CancellationToken.None).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<StopEntry>();
        }

        var term = query.Trim();
        var termKey = Normalize(term);
        var results = _stops
            .Where(s => s.SearchKey.Contains(termKey, StringComparison.OrdinalIgnoreCase)
                        || s.StopIds.Any(id => id.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(s => s.Name)
            .ThenBy(s => s.StopIds.FirstOrDefault())
            .Take(limit)
            .ToList();

        return results;
    }

    private async Task LoadStopsAsync(CancellationToken cancellationToken)
    {
        // Nejprve zkusime cache, aby UI melo data i bez site.
        var loadedFromCache = await TryLoadStopsFromCacheAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await DownloadStopsAsync(cancellationToken).ConfigureAwait(false);
            _lastDownloadUtc = DateTimeOffset.UtcNow;
            IsLoaded = true;
        }
        catch
        {
            if (loadedFromCache)
            {
                IsLoaded = true;
                return;
            }

            IsLoaded = false;
            throw;
        }
    }

    private async Task DownloadStopsAsync(CancellationToken cancellationToken)
    {
        using var stream = await _httpClient.GetStreamAsync(GtfsUrl, cancellationToken).ConfigureAwait(false);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        memory.Position = 0;

        _stops.Clear();
        LoadStopsFromArchive(memory, cancellationToken);
        await _cache.SaveStopsAsync(_stops, cancellationToken).ConfigureAwait(false);
        IsLoaded = true;
    }

    private void LoadStopsFromArchive(Stream archiveStream, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("stops.txt");
        if (entry is null)
        {
            throw new InvalidOperationException("stops.txt v GTFS balicku nenalezen.");
        }

        using var entryStream = entry.Open();
        using var parser = new TextFieldParser(entryStream, Encoding.UTF8)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true
        };
        parser.SetDelimiters(",");

        var header = parser.ReadFields() ?? Array.Empty<string>();
        var idxStopId = Array.IndexOf(header, "stop_id");
        var idxName = Array.IndexOf(header, "stop_name");
        var idxLocationType = Array.IndexOf(header, "location_type");
        var idxParentStation = Array.IndexOf(header, "parent_station");

        if (idxStopId < 0 || idxName < 0)
        {
            throw new InvalidOperationException("Hlavicka stops.txt neobsahuje stop_id/stop_name.");
        }

        var byKey = new Dictionary<string, StopEntry>(StringComparer.OrdinalIgnoreCase);

        while (!parser.EndOfData)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fields = parser.ReadFields();
            if (fields is null || fields.Length <= Math.Max(idxStopId, idxName))
            {
                continue;
            }

            var locationType = idxLocationType >= 0 && fields.Length > idxLocationType ? fields[idxLocationType] : string.Empty;
            var parentStation = idxParentStation >= 0 && fields.Length > idxParentStation ? fields[idxParentStation] : string.Empty;
            var stopId = fields[idxStopId];
            var stopName = fields[idxName];

            // Jen skutecne zastavkove body, ne parent stanice (location_type 1).
            if (!string.IsNullOrWhiteSpace(locationType) && locationType.Trim() == "1")
            {
                continue;
            }

            // Skupinu stavime podle nazvu zastavky, aby se spojily i ruzne parent_station (napr. metro + povrch).
            var key = Normalize(stopName);

            if (!byKey.TryGetValue(key, out var stop))
            {
                stop = new StopEntry
                {
                    Name = stopName,
                    ParentId = string.IsNullOrWhiteSpace(parentStation) ? null : parentStation.Trim(),
                    StopIds = new List<string>(),
                    SourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                };
                stop.SearchKey = Normalize(stop.Name);
                byKey[key] = stop;
                _stops.Add(stop);
            }

            if (!string.IsNullOrWhiteSpace(stopName))
            {
                stop.SourceNames.Add(stopName.Trim());
            }

            if (!stop.StopIds.Contains(stopId, StringComparer.OrdinalIgnoreCase))
            {
                stop.StopIds.Add(stopId);
            }
        }

        IsLoaded = true;
    }

    private async Task<bool> TryLoadStopsFromCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            var cached = await _cache.LoadStopsAsync(cancellationToken).ConfigureAwait(false);
            if (cached?.Stops?.Count > 0)
            {
                var freshEnough = DateTimeOffset.UtcNow - cached.SavedAt <= StopsCacheMaxAge;

                _stops.Clear();
                foreach (var stop in cached.Stops)
                {
                    stop.SearchKey = string.IsNullOrWhiteSpace(stop.SearchKey) ? Normalize(stop.Name) : stop.SearchKey;
                    _stops.Add(stop);
                }

                _lastDownloadUtc = cached.SavedAt;
                IsLoaded = true;
                return freshEnough || _stops.Count > 0;
            }
        }
        catch
        {
            // Ignoruj chyby cache, zkusime download.
        }

        return false;
    }

    private bool ShouldRefreshStops()
    {
        if (!_lastDownloadUtc.HasValue)
        {
            return true;
        }

        return DateTimeOffset.UtcNow - _lastDownloadUtc.Value >= StopsRefreshInterval;
    }

    private static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
