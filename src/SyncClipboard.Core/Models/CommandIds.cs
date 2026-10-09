namespace SyncClipboard.Core.Models;

public static class CommandIds
{
    public const string OpenMainUI = "OpenMainUI";
    public const string CompletelyExit = "CompletelyExit";
    public const string SwitchClipboardSyncing = "SwitchClipboardSyncing";
    public const string SwitchBuiltInServer = "SwitchBuiltInServer";
    public const string UploadOnce = "UploadOnce";
    public const string CopyAndUpload = "CopyAndUpload";
    public const string UploadWithoutFilter = "UploadWithoutFilter";
    public const string CopyAndUploadWithoutFilter = "CopyAndUploadWithoutFilter";
    public const string DownloadOnce = "DownloadOnce";
    public const string DownloadAndPaste = "DownloadAndPaste";
    public const string SwitchEasyCopyImage = "SwitchEasyCopyImage";
    public const string OpenHistoryPanel = "OpenHistoryPanel";
    public const string ToggleHistoryPanel = "ToggleHistoryPanel";

    private static readonly Dictionary<string, string> LegacyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["6DB18835-1DAD-0495-E126-45F5D2D193A7"] = OpenMainUI,
        ["2F30872E-B412-F580-7C20-F0D063A85BE0"] = CompletelyExit,
        ["26D8A39E-F50D-CC71-FE15-647F67FDB2F9"] = SwitchClipboardSyncing,
        ["145740F4-03F7-6F6C-5B93-B027C7C49C59"] = SwitchBuiltInServer,
        ["D0EDB9A4-3409-4A76-BC2B-4C0CD80DD850"] = UploadOnce,
        ["D13672E9-D14C-4D48-847E-10B030F4B608"] = CopyAndUpload,
        ["6C5314DF-B504-25EA-074D-396E5C69BAF1"] = UploadWithoutFilter,
        ["40E0B462-FCED-C4CD-7126-1F5204443DC1"] = CopyAndUploadWithoutFilter,
        ["95396FFF-E5FE-45D3-9D70-4A43FA34FF31"] = DownloadOnce,
        ["8a4a033e-31da-1b87-76ea-548885866b66"] = DownloadAndPaste,
        ["337275BE-57A2-2E97-6096-FF3D087D8A9C"] = SwitchEasyCopyImage,
    };

    public static string Resolve(string id) => LegacyAliases.TryGetValue(id, out var name) ? name : id;
}
