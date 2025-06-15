namespace SyncClipboard.Core.ViewModels;

public record CommandCollectionViewModel(string Name, string FontIcon, List<UniqueCommandViewModel>? Commands);