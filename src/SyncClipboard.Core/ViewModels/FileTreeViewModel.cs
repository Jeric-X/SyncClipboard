using CommunityToolkit.Mvvm.ComponentModel;

namespace SyncClipboard.Core.ViewModels;

public partial class FileTreeViewModel(string fullPath, string name, bool isFolder) : ObservableObject
{
    public string FullPath { get; } = fullPath;
    public string Name { get; } = name;
    public bool IsFolder { get; } = isFolder;

    [ObservableProperty]
    public List<FileTreeViewModel>? children;
}
