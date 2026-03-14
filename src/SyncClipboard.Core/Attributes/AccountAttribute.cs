namespace SyncClipboard.Core.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class AccountConfigTypeAttribute(string name) : Attribute
{
    public string Name { get; set; } = name;
    public int Priority { get; set; } = int.MaxValue;

    public string GetName()
    {
        return Name;
    }
}