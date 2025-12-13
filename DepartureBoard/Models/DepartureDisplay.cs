namespace DepartureBoard.Models;

public class DepartureDisplay
{
    public string Line { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string StopName { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string DepartureTime { get; set; } = string.Empty;
    public string Countdown { get; set; } = string.Empty;
    public string Delay { get; set; } = string.Empty;
    public DateTimeOffset When { get; set; }
    public string Accessibility { get; set; } = string.Empty;
    public string VehicleType { get; set; } = string.Empty;
}
