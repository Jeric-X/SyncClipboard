namespace SyncClipboard.Core.Commons;

[AttributeUsage(AttributeTargets.Class)]
public class AccountConfigAttribute(string name) : Attribute
{
    public string Name { get; } = name;

    public string GetName()
    {
        return Name;
    }
}