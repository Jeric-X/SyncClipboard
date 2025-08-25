using SyncClipboard.Abstract;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.History;

namespace SyncClipboard.Core.UserServices.ClipboardService;

public class HistoryService(HistoryManager historyManager) : ClipboardHander
{
    public override string SERVICE_NAME => "History Service";

    public override string LOG_TAG => "History";

    protected override bool SwitchOn { get => true; set => throw new NotImplementedException(); }

    protected override async Task HandleClipboard(ClipboardMetaInfomation clipboardMetaInfomation, Profile profile, CancellationToken token)
    {
        if (profile.Type == ProfileType.Unknown)
        {
            return;
        }
        await historyManager.AddHistory(profile.CreateHistoryRecord(), token);
    }
}