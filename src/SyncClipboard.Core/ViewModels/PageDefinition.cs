namespace SyncClipboard.Core.ViewModels;

public class PageDefinition(string name, string title, string? fontIcon = null)
{
    public string Name { get; set; } = name;
    public string Title { get; set; } = title;
    public string? FontIcon { get; set; } = fontIcon ?? "\uE115";

    public static readonly PageDefinition SyncSetting = new("SyncSetting", I18n.Strings.Syncing, "\uEBD3");
    public static readonly PageDefinition ServerConfig = new("ServerConfig", I18n.Strings.Server, "\uE753");
    public static readonly PageDefinition CliboardAssistant = new("CliboardAssistant", I18n.Strings.Assistant, "\uF406");
    public static readonly PageDefinition HistorySetting = new("HistorySetting", I18n.Strings.ClipboardHistory, "\uE1D3");
    public static readonly PageDefinition ServiceStatus = new("ServiceStatus", I18n.Strings.Status, "\uE9D2");
    public static readonly PageDefinition SystemSetting = new("SystemSetting", I18n.Strings.SystemSettings, "\uE115");
    public static readonly PageDefinition About = new("About", I18n.Strings.About, "\uE946");
    public static readonly PageDefinition Diagnose = new("Diagnose", I18n.Strings.Diagnose, "\uE9D9");
    public static readonly PageDefinition License = new("License", I18n.Strings.License);
    public static readonly PageDefinition NextCloudLogIn = new("NextCloudLogIn", I18n.Strings.UseNextcloud);
    public static readonly PageDefinition AddAccount = new("AddAccount", I18n.Strings.AddAccount);
    public static readonly PageDefinition DefaultAddAccount = new("AccountConfigEdit", I18n.Strings.EditAccountConfig);
    public static readonly PageDefinition FileSyncFilterSetting = new("FileSyncFilterSetting", I18n.Strings.FileSyncFilter);
    public static readonly PageDefinition SyncContentControl = new("SyncContentControl", I18n.Strings.SyncContentControl);
    public static readonly PageDefinition Hotkey = new("Hotkey", I18n.Strings.Hotkeys, "\uE144");

    public override bool Equals(object? obj)
    {
        if (obj == null) return false;
        if (obj is not PageDefinition pageObj) return false;

        return this.Name == pageObj.Name;
    }

    public override int GetHashCode()
    {
        return Name.GetHashCode();
    }
}
