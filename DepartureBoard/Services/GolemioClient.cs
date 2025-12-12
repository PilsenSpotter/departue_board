using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using DepartureBoard.Models;

namespace DepartureBoard.Services;

public class GolemioClient
{
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
        ApiKey = apiKey ?? Environment.GetEnvironmentVariable("GOLEMIO_API_KEY");
    }

    public async Task<IReadOnlyList<Departure>> GetDeparturesAsync(
        string stopId,
        string? apiKey = null,
        int minutesAfter = 20,
        int limit = 60,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stopId))
        {
            throw new ArgumentException("Zadej ID zastávky (např. U215Z2P).", nameof(stopId));
        }

        var token = string.IsNullOrWhiteSpace(apiKey) ? ApiKey : apiKey;

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Chybí API klíč. Nastav proměnnou GOLEMIO_API_KEY nebo ho zadej v aplikaci.");
        }

        var uri =
            $"https://api.golemio.cz/v2/pid/departureboards?ids[]={Uri.EscapeDataString(stopId)}&minutesAfter={minutesAfter}&limit={limit}&preferredTimezone=Europe%2FPrague";

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("X-Access-Token", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Golemio API vrátilo {(int)response.StatusCode}: {body}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<DepartureBoardResponse>(stream, _jsonOptions, cancellationToken);

        return payload?.Departures ?? new List<Departure>();
    }
}
