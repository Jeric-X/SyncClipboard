namespace SyncClipboard.Core.ViewModels;

public class PageDefinition
{
    public string Name { get; set; }
    public string Title { get; set; }

    public static readonly PageDefinition SyncSetting = new("SyncSetting", "剪切板同步");
    public static readonly PageDefinition CliboardAssistant = new("CliboardAssistant", "剪切板助手");
    public static readonly PageDefinition ServiceStatus = new("ServiceStatus", "服务状态");
    public static readonly PageDefinition SystemSetting = new("SystemSetting", "系统设置");
    public static readonly PageDefinition About = new("About", "关于");
    public static readonly PageDefinition License = new("License", "License");

    private PageDefinition(string name, string title)
    {
        Name = name;
        Title = title;
    }

    public override bool Equals(object? obj)
    {
        if (obj == null) return false;
        if (obj is not PageDefinition pageObj) return false;

        return this == pageObj;
    }

    public override int GetHashCode()
    {
        return Name.GetHashCode();
    }
}
