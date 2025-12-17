namespace DepartureBoard.Models;

public class UserSettings
{
    public bool RememberSettings { get; set; } = true;
    public List<StopEntry> SelectedStops { get; set; } = new();
    public int MinutesAfter { get; set; } = 20;
    public int RefreshSeconds { get; set; } = 5;
    public bool IsLightTheme { get; set; }
    public bool ShowBus { get; set; } = true;
    public bool ShowTram { get; set; } = true;
    public bool ShowMetro { get; set; } = true;
    public bool ShowTrain { get; set; } = true;
    public bool ShowTrolley { get; set; } = true;
    public AccessibilityFilter AccessibilityFilter { get; set; } = AccessibilityFilter.All;
    public bool IsDisplayMode { get; set; }
    public bool IsBoardMode { get; set; }
}
