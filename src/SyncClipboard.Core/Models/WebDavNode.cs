namespace SyncClipboard.Core.Models;

public class WebDavNode
{
    public string FullPath;
    public string Name;
    public bool IsFolder;
    public List<WebDavNode>? Children;
    public WebDavNode(string fullPath, string name, bool isFolder, List<WebDavNode>? children = null)
    {
        FullPath = fullPath;
        Name = name;
        IsFolder = isFolder;
        Children = children;
    }
}