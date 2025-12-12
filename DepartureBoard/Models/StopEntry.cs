using System.Linq;

namespace DepartureBoard.Models;

public class StopEntry
{
    public string Name { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public List<string> StopIds { get; set; } = new();
    public HashSet<string> SourceNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string SearchKey { get; set; } = string.Empty;

    public string PrimaryId => StopIds.FirstOrDefault() ?? string.Empty;
    public string DisplayIds => string.Join(", ", StopIds);

    public override string ToString() => $"{Name} ({string.Join(", ", StopIds)})";
}
