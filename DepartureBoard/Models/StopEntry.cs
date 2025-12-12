namespace DepartureBoard.Models;

public class StopEntry
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? PlatformCode { get; set; }
    public string SearchKey { get; set; } = string.Empty;

    public override string ToString() => $"{Name} ({Id})";
}
