namespace SyncClipboard.Core.Models;

public record class LanguageModel
{
    public string TranslatedName { get; }
    public string LocaleTag { get; }
    public bool IsDefault { get; }
    public string DisplayName => IsDefault ? TranslatedName : $"{TranslatedName} ({LocaleTag})";

    public LanguageModel(string localName, string tag, bool isDefault = false)
    {
        TranslatedName = localName;
        LocaleTag = tag;
        IsDefault = isDefault;
    }
}