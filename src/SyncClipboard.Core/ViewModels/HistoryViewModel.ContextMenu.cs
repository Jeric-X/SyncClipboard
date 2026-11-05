using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.ViewModels.Sub;

namespace SyncClipboard.Core.ViewModels;

public partial class HistoryViewModel
{
    public (string Text, IRelayCommand Command, object? Parameter)[] BuildHistoryMenuCommands(HistoryRecordVM record)
    {
        if (IsMultiSelecting)
        {
            return
            [
                (Strings.HistoryMultiSelectDelete, ConfirmDeleteSelectedCommand, null),
                (AreSelectedRecordsStarred ? Strings.HistoryMultiSelectUnstar : Strings.HistoryMultiSelectStar,
                    ConfirmToggleSelectedStarredCommand, null),
                (Strings.HistoryExportSelected, ExportSelectedCommand, null),
                (Strings.HistoryMultiSelectAll, ToggleCurrentFilterSelectionCommand, null),
                (Strings.HistoryMultiSelectExit, ExitMultiSelectCommand, null)
            ];
        }

        return
        [
            (Strings.HistoryMultiSelectDelete, DeleteItemCommand, record),
            (record.Stared ? Strings.HistoryMultiSelectUnstar : Strings.HistoryMultiSelectStar, ChangeStarStatusCommand, record),
            (Strings.HistoryExportSelected, ExportRecordCommand, record),
            (Strings.HistoryMultiSelectEnter, EnterMultiSelectCommand, record)
        ];
    }

    [RelayCommand]
    private Task ExportRecordAsync(HistoryRecordVM record) =>
        _serviceProvider.GetRequiredService<IHistoryExportDialog>().ShowAsync([record.Key], fromHistoryWindow: true);

    public List<MenuItem> BuildDiagnosticActions(HistoryRecordVM record)
    {
        var actions = new List<MenuItem>();
        var isDiagnoseMode = _configManager.GetConfig<ProgramConfig>().DiagnoseMode;
        if (isDiagnoseMode)
        {
            actions.Add(new MenuItem($"Hash: {record.Hash}", async () =>
            {
                try
                {
                    var profile = new TextProfile(record.Hash);
                    await RunWithOperationTimeoutAsync(
                        "copy history hash",
                        token => localClipboardSetter.Set(profile, token));
                }
                catch { }
            }));
        }

        return actions;
    }
}
