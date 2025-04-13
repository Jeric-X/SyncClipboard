using SyncClipboard.Core.Models;
using System.Globalization;

namespace SyncClipboard.Core.I18n;

public class I18nHelper
{
    public static readonly LanguageModel[] SupportedLanguage =
    [
        new LanguageModel(Strings.DefaultLanguage, "", true),
        new LanguageModel("English", "en-US"),
        new LanguageModel("简体中文", "zh-CN")
    ];

    private static CultureInfo? DefaultUICulture;
    public static void SaveDefaultUICultureInfo()
    {
        DefaultUICulture = CultureInfo.CurrentUICulture;
    }

    public static string? GetChangingLanguageInfo(LanguageModel language)
    {
        return Strings.ResourceManager.GetString(
            nameof(Strings.ChangingLangInfo),
            language.IsDefault ? DefaultUICulture : new CultureInfo(language.LocaleTag))!;
    }

    public static void SetProgramLanguage(string languageTag)
    {
        var oldCulture = CultureInfo.CurrentUICulture;
        Interlocked.CompareExchange(ref DefaultUICulture, oldCulture, null);
        var newCulture = new CultureInfo(languageTag);
        CultureInfo.CurrentUICulture = newCulture;
        CultureInfo.DefaultThreadCurrentUICulture = newCulture;
    }
}
