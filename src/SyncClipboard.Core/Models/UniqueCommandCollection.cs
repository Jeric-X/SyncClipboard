namespace SyncClipboard.Core.Models;

public record class UniqueCommandCollection(string Name)
{
    public List<UniqueCommand> Commands { get; } = new List<UniqueCommand>();
}
