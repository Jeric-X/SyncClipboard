using NativeNotification.Interface;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities;
using System.Text.RegularExpressions;

namespace SyncClipboard.Core.Clipboard;

public partial class ProfileActionBuilder(LocalClipboardSetter setter)
{
    public List<MenuItem> Build(Profile profile)
    {
        List<MenuItem> actions =
        [
            new MenuItem(Strings.Copy, () => { _ = setter.Set(profile, CancellationToken.None); }),
        ];

        switch (profile)
        {
            case TextProfile textProfile:
                if (HasUrl(textProfile.Text, out var url) && url is not null)
                {
                    actions.Add(new MenuItem(Strings.OpenInBrowser, () => Sys.OpenWithDefaultApp(url)));
                }
                break;

            case GroupProfile groupProfile:
                {
                    string? folder = null;
                    if (groupProfile.Files.Length > 0)
                    {
                        folder = Path.GetDirectoryName(groupProfile.Files[0]);
                    }
                    if (string.IsNullOrEmpty(folder) && !string.IsNullOrEmpty(groupProfile.FullPath))
                    {
                        folder = Path.GetDirectoryName(groupProfile.FullPath);
                    }
                    if (!string.IsNullOrEmpty(folder))
                    {
                        var openFolder = folder!;
                        actions.Add(new MenuItem(Strings.OpenFolder, () => Sys.OpenFolderInFileManager(openFolder)));
                    }
                }
                break;

            case FileProfile fileProfile:
                {
                    var fullPath = fileProfile.GetLocalDataPath();
                    actions.Add(new MenuItem(Strings.OpenFolder, () => Sys.ShowPathInFileManager(fullPath)));
                    actions.Add(new MenuItem(Strings.Open, () => Sys.OpenWithDefaultApp(fullPath)));
                }
                break;
        }

        return actions;
    }

    [GeneratedRegex("http(s)?://[a-zA-Z0-9\\-_]+(\\.[a-zA-Z0-9\\-_]+)+(:[0-9]{1,5})?(/[a-zA-Z0-9\\-_.?=&%]*)*", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    private static bool HasUrl(string str, out string? url)
    {
        url = null;
        try
        {
            var match = UrlRegex().Match(str);
            if (match.Success)
            {
                url = match.Value;
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static List<ActionButton> ToActionButtons(IEnumerable<MenuItem> items)
    {
        return items
            .Where(i => i.Text is not null)
            .Select(i => new ActionButton(i.Text!, () => i.Action?.Invoke()))
            .ToList();
    }
}


