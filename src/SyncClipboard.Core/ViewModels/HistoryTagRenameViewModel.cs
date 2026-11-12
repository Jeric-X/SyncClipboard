using CommunityToolkit.Mvvm.ComponentModel;
using SyncClipboard.Core.I18n;

namespace SyncClipboard.Core.ViewModels;

public partial class HistoryTagRenameViewModel(string name, Func<string, bool, Task<bool>> save) : ObservableObject
{
    private string? mergeName;

    [ObservableProperty]
    private string newName = name;

    [ObservableProperty]
    private string message = string.Empty;

    partial void OnNewNameChanged(string value)
    {
        mergeName = null;
        Message = string.Empty;
    }

    public async Task<bool> SaveAsync()
    {
        try
        {
            var normalized = HistoryTags.NormalizeName(NewName);
            if (await save(normalized, mergeName == normalized))
                return true;
            mergeName = normalized;
            Message = string.Format(Strings.HistoryTagMergeConfirmation, normalized);
        }
        catch (Exception ex)
        {
            mergeName = null;
            Message = ex is ArgumentException ? Strings.HistoryTagsInvalid : ex.Message;
        }
        return false;
    }
}
