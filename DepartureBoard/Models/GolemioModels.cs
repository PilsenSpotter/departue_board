using System.Text.Json.Serialization;

namespace DepartureBoard.Models;

public class DepartureBoardResponse
{
    [JsonPropertyName("departures")]
    public List<Departure> Departures { get; set; } = new();
}

public class Departure
{
    [JsonPropertyName("route")]
    public Route? Route { get; set; }

    [JsonPropertyName("trip")]
    public Trip? Trip { get; set; }

    [JsonPropertyName("stop")]
    public Stop? Stop { get; set; }

    [JsonPropertyName("departure_timestamp")]
    public DepartureTimestamp? DepartureTimestamp { get; set; }

    [JsonPropertyName("delay")]
    public DelayInfo? Delay { get; set; }
}

public class Route
{
    [JsonPropertyName("short_name")]
    public string? ShortName { get; set; }
}

public class Trip
{
    [JsonPropertyName("headsign")]
    public string? Headsign { get; set; }
}

public class Stop
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("platform_code")]
    public string? PlatformCode { get; set; }
}

public class DepartureTimestamp
{
    [JsonPropertyName("predicted")]
    public DateTimeOffset? Predicted { get; set; }

    [JsonPropertyName("scheduled")]
    public DateTimeOffset? Scheduled { get; set; }

    [JsonPropertyName("actual")]
    public DateTimeOffset? Actual { get; set; }

    public DateTimeOffset? Effective => Predicted ?? Actual ?? Scheduled;
}

public class DelayInfo
{
    [JsonPropertyName("total")]
    public int? Total { get; set; }

    [JsonPropertyName("arrival")]
    public int? Arrival { get; set; }

    [JsonPropertyName("departure")]
    public int? Departure { get; set; }
}
