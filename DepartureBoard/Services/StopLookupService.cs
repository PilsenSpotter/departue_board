using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using DepartureBoard.Models;
using Microsoft.VisualBasic.FileIO;
using System.Globalization;

namespace DepartureBoard.Services;

public class StopLookupService
{
    private const string GtfsUrl = "https://data.pid.cz/PID_GTFS.zip";
    private readonly HttpClient _httpClient = new();
    private readonly List<StopEntry> _stops = new();
    private Task? _loadTask;
    public bool IsLoaded { get; private set; }

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoaded)
        {
            return;
        }

        if (_loadTask == null || _loadTask.IsCanceled || _loadTask.IsFaulted)
        {
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
                        || s.Id.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Name)
            .ThenBy(s => s.Id)
            .Take(limit)
            .ToList();

        return results;
    }

    private async Task LoadStopsAsync(CancellationToken cancellationToken)
    {
        using var stream = await _httpClient.GetStreamAsync(GtfsUrl, cancellationToken).ConfigureAwait(false);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        memory.Position = 0;

        using var archive = new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.GetEntry("stops.txt");
        if (entry is null)
        {
            throw new InvalidOperationException("stops.txt v GTFS balíku nenalezen.");
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
        var idxPlatform = Array.IndexOf(header, "platform_code");

        if (idxStopId < 0 || idxName < 0)
        {
            throw new InvalidOperationException("Hlavička stops.txt neobsahuje stop_id/stop_name.");
        }

        while (!parser.EndOfData)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fields = parser.ReadFields();
            if (fields is null || fields.Length <= Math.Max(idxStopId, idxName))
            {
                continue;
            }

            var stop = new StopEntry
            {
                Id = fields[idxStopId],
                Name = fields[idxName],
                PlatformCode = idxPlatform >= 0 && fields.Length > idxPlatform ? fields[idxPlatform] : null
            };
            stop.SearchKey = Normalize(stop.Name);

            _stops.Add(stop);
        }

        IsLoaded = true;
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
