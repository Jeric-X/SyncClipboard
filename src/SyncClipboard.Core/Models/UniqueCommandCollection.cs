namespace SyncClipboard.Core.Models;

public record class UniqueCommandCollection(string Name, string FontIcon)
{
    public List<UniqueCommand> Commands { get; init; } = new List<UniqueCommand>();
}
