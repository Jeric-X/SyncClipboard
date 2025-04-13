namespace SyncClipboard.Core.Models;

public record class WebDavNode(string FullPath, string Name, bool IsFolder, List<WebDavNode>? Children = null);