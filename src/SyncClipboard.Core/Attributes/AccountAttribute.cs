namespace SyncClipboard.Core.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class AccountConfigTypeAttribute(string name) : Attribute
{
    public string Name { get; } = name;

    public string GetName()
    {
        return Name;
    }
}