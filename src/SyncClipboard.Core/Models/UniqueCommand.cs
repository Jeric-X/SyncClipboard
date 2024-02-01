namespace SyncClipboard.Core.Models;

public record class UniqueCommand(string Name, Guid Guid, Action Command);