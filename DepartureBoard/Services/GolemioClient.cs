using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using DepartureBoard.Models;

namespace DepartureBoard.Services;

public class GolemioClient
{
    // PASTE your API key here for fixed usage in the app.
    private const string EmbeddedApiKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpZCI6NDM2MSwiaWF0IjoxNzY1NDgzODc2LCJleHAiOjExNzY1NDgzODc2LCJpc3MiOiJnb2xlbWlvIiwianRpIjoiN2U3M2Q5Y2QtZjNhOS00NjkxLWIyYjctNjZhYzNhN2ZkZWM0In0.bJU62Th7j1PYkQclHHqH-m0kx3y7qyWnuWjs3_jPp6g";

    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public string? ApiKey { get; set; }

    public GolemioClient(HttpClient? httpClient = null, string? apiKey = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        ApiKey = apiKey ?? EmbeddedApiKey ?? Environment.GetEnvironmentVariable("GOLEMIO_API_KEY");
    }

    public async Task<IReadOnlyList<Departure>> GetDeparturesAsync(
        IEnumerable<string> stopIds,
        string? apiKey = null,
        int minutesAfter = 20,
        int limit = 60,
        CancellationToken cancellationToken = default)
    {
        var ids = (stopIds ?? Array.Empty<string>())
            .Select(id => id?.Trim() ?? string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ids.Count == 0)
        {
            throw new ArgumentException("Zadej alespon jedno stop_id (napr. U215Z2P).", nameof(stopIds));
        }

        var token = string.IsNullOrWhiteSpace(apiKey) ? ApiKey : apiKey;
        token = token?.Trim();

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Chybi API klic. Nastav EmbeddedApiKey v GolemioClientu nebo promennou GOLEMIO_API_KEY.");
        }

        var baseUrls = new[]
        {
            "https://api.golemio.cz/v2/pid/departureboards",
            "https://api.golemio.cz/v2/departureboards"
        };
        var idPatterns = new[]
        {
            "ids={0}",     // dokumentace v2/pid/departureboards
            "ids[]={0}"    // nektere priklady uvadeji pole ids[]
        };

        var errors = new List<string>();
        bool anyNotFound = false;

        var stopTrying = false;

        foreach (var baseUrl in baseUrls)
        {
            if (stopTrying) break;

            foreach (var pattern in idPatterns)
            {
                var idPart = string.Join("&", ids.Select(id => string.Format(pattern, Uri.EscapeDataString(id))));
                var uri =
                    $"{baseUrl}?{idPart}&minutesAfter={minutesAfter}&limit={limit}&preferredTimezone=Europe%2FPrague";

                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.TryAddWithoutValidation("X-Access-Token", token);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var response = await _httpClient.SendAsync(request, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    var payload = await JsonSerializer.DeserializeAsync<DepartureBoardResponse>(stream, _jsonOptions, cancellationToken);
                    return payload?.Departures ?? new List<Departure>();
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                errors.Add($"{uri} -> {(int)response.StatusCode}: {body}");

                if (response.StatusCode != HttpStatusCode.NotFound)
                {
                    stopTrying = true;
                    break;
                }
                anyNotFound = true;
            }
        }

        if (anyNotFound)
        {
            throw new HttpRequestException($"Golemio API: zadana zastavka nebyla nalezena (zkus jinou platformu / P kod). Detaily: {string.Join(" | ", errors)}");
        }

        throw new HttpRequestException($"Golemio API neuspesne ({string.Join(" | ", errors)})");
    }
}
