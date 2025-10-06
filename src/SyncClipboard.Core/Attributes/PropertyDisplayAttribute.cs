namespace SyncClipboard.Core.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public class PropertyDisplayAttribute(string displayName) : Attribute
{
    public string DisplayName { get; } = displayName;
    public bool IsPassword { get; set; } = false;
    public string? Description { get; set; }
}