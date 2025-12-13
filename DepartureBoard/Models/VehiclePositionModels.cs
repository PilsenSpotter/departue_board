using System.Text.Json.Serialization;

namespace DepartureBoard.Models;

public class VehiclePositionsResponse
{
    [JsonPropertyName("features")]
    public List<VehiclePositionFeature> Features { get; set; } = new();
}

public class VehiclePositionFeature
{
    [JsonPropertyName("properties")]
    public VehiclePositionProperties? Properties { get; set; }
}

public class VehiclePositionProperties
{
    [JsonPropertyName("trip")]
    public VehicleTrip? Trip { get; set; }
}

public class VehicleTrip
{
    [JsonPropertyName("gtfs")]
    public VehicleTripGtfs? Gtfs { get; set; }

    [JsonPropertyName("vehicle_type")]
    public VehicleType? VehicleType { get; set; }

    [JsonPropertyName("vehicle_descriptor")]
    public VehicleDescriptor? VehicleDescriptor { get; set; }

    [JsonPropertyName("wheelchair_accessible")]
    public bool? WheelchairAccessible { get; set; }
}

public class VehicleTripGtfs
{
    [JsonPropertyName("trip_id")]
    public string? TripId { get; set; }
}

public class VehicleDescriptor
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("manufacturer")]
    public string? Manufacturer { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("description_cs")]
    public string? DescriptionCs { get; set; }

    [JsonPropertyName("description_en")]
    public string? DescriptionEn { get; set; }
}

public class VehicleType
{
    [JsonPropertyName("description_cs")]
    public string? DescriptionCs { get; set; }

    [JsonPropertyName("description_en")]
    public string? DescriptionEn { get; set; }

    [JsonPropertyName("id")]
    public int? Id { get; set; }
}

public class VehiclePositionInfo
{
    public string? DisplayName { get; set; }
    public bool? WheelchairAccessible { get; set; }
}
