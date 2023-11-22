namespace SyncClipboard.Core.ViewModels;

public class PageDefinition
{
    public string Name { get; set; }
    public string Title { get; set; }
    public string? FontIcon { get; set; }

    public static readonly PageDefinition SyncSetting = new("SyncSetting", I18n.Strings.Syncing, "\uEBD3");
    public static readonly PageDefinition CliboardAssistant = new("CliboardAssistant", I18n.Strings.Assistant, "\uF406");
    public static readonly PageDefinition ServiceStatus = new("ServiceStatus", I18n.Strings.Status, "\uE9D2");
    public static readonly PageDefinition SystemSetting = new("SystemSetting", I18n.Strings.SystemSettings, "\uE115");
    public static readonly PageDefinition About = new("About", I18n.Strings.About, "\uE946");
    public static readonly PageDefinition Diagnose = new("Diagnose", I18n.Strings.Diagnose, "\uE9D9");
    public static readonly PageDefinition License = new("License", I18n.Strings.License);
    public static readonly PageDefinition NextCloudLogIn = new("NextCloudLogIn", I18n.Strings.UseNextcloud);

    public PageDefinition(string name, string title, string? fontIcon = null)
    {
        Name = name;
        Title = title;
        FontIcon = fontIcon ?? "\uE115";
    }

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
