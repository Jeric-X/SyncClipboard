using SyncClipboard.Core.Interfaces;
using System.Collections.Generic;

namespace SyncClipboard.Desktop.Utilities;

internal class FontManager : IFontManager
{
    public List<string> GetInstalledFontNames()
    {
        List<string> fontNames = [];
        foreach (var item in Avalonia.Media.FontManager.Current.SystemFonts)
        {
            // item.Key
            fontNames.Add(item.Name);
        }
        return fontNames;
    }
}
