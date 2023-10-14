namespace SyncClipboard.Core.ViewModels;

public class PageDefinition
{
    public string Name { get; set; }
    public string Title { get; set; }

    public static readonly PageDefinition SyncSetting = new("SyncSetting", I18n.Strings.Syncing);
    public static readonly PageDefinition CliboardAssistant = new("CliboardAssistant", I18n.Strings.Assistant);
    public static readonly PageDefinition ServiceStatus = new("ServiceStatus", I18n.Strings.Status);
    public static readonly PageDefinition SystemSetting = new("SystemSetting", I18n.Strings.Settings);
    public static readonly PageDefinition About = new("About", I18n.Strings.About);
    public static readonly PageDefinition License = new("License", "License");
    public static readonly PageDefinition NextCloudLogIn = new("NextCloudLogIn", "登录到Nextcloud");

    private PageDefinition(string name, string title)
    {
        Name = name;
        Title = title;
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
