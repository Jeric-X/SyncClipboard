using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.History;

namespace SyncClipboard.Core.UserServices.ClipboardService;

public class HistoryService(HistoryManager historyManager, ConfigManager configManager) : ClipboardHander
{
    public override string SERVICE_NAME => I18n.Strings.ClipboardHistory;

    public override string LOG_TAG => "History";
    protected override bool SwitchOn
    {
        get => configManager.GetConfig<HistoryConfig>().EnableHistory;
        set => configManager.SetConfig(configManager.GetConfig<HistoryConfig>() with { EnableHistory = value });
    }

    protected override async Task HandleClipboard(ClipboardMetaInfomation clipboardMetaInfomation, Profile profile, CancellationToken token)
    {
        if (profile.Type == ProfileType.Unknown)
        {
            return;
        }

        if (profile is TextProfile textProfile && string.IsNullOrEmpty(textProfile.Text))
        {
            return;
        }

        if (clipboardMetaInfomation.Effects.HasValue &&
            clipboardMetaInfomation.Effects.Value.HasFlag(DragDropEffects.Move))
        {
            return;
        }

        await historyManager.AddLocalHistory(await profile.ToHistoryRecord(token), token);
    }
}