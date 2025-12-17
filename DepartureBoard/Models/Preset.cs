namespace DepartureBoard.Models;

public class Preset
{
    public string Name { get; set; } = string.Empty;
    public List<StopEntry> Stops { get; set; } = new();
    public int MinutesAfter { get; set; } = 20;
    public int RefreshSeconds { get; set; } = 5;
    public bool ShowBus { get; set; } = true;
    public bool ShowTram { get; set; } = true;
    public bool ShowMetro { get; set; } = true;
    public bool ShowTrain { get; set; } = true;
    public bool ShowTrolley { get; set; } = true;
    public AccessibilityFilter AccessibilityFilter { get; set; } = AccessibilityFilter.All;
    public bool ShowOnTimeOnly { get; set; }
}
