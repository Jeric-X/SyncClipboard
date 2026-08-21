using NativeNotification.Interface;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.Clipboard;

public partial class ProfileActionBuilder(LocalClipboardSetter setter, IProfileEnv profileEnv)
{
    public async Task<List<MenuItem>> Build(Profile profile, CancellationToken token)
    {
        List<MenuItem> actions =
        [
            new MenuItem(Strings.Copy, () => { _ = setter.Set(profile, CancellationToken.None); }),
        ];

        var localInfo = await profile.Localize(profileEnv.GetPersistentDir(), true, token);

        if (HasUrl(localInfo.Text, out var url) && url is not null)
        {
            actions.Add(new MenuItem(Strings.OpenInBrowser, () => Sys.OpenWithDefaultApp(url)));
        }

        if (localInfo.FilePaths.Length > 0)
        {
            string? folder = Path.GetDirectoryName(localInfo.FilePaths[0]);
            if (!string.IsNullOrEmpty(folder))
            {
                actions.Add(new MenuItem(Strings.OpenFolder, () => Sys.OpenFolderInFileManager(folder)));
            }

            if (localInfo.FilePaths.Length == 1)
            {
                var fullPath = localInfo.FilePaths[0];
                actions.Add(new MenuItem(Strings.Open, () => Sys.OpenWithDefaultApp(fullPath)));
            }
        }

        return actions;
    }

    public async Task<MenuItem?> GetPrimaryAction(Profile profile, CancellationToken token)
    {
        var localInfo = await profile.Localize(profileEnv.GetPersistentDir(), true, token);

        if (profile is TextProfile && HasUrl(localInfo.Text, out var url) && url is not null)
        {
            return new MenuItem(Strings.OpenInBrowser, () => Sys.OpenWithDefaultApp(url));
        }

        if (profile is FileProfile && localInfo.FilePaths.Length == 1)
        {
            var fullPath = localInfo.FilePaths[0];
            return new MenuItem(Strings.Open, () => Sys.OpenWithDefaultApp(fullPath));
        }

        if (profile is GroupProfile && localInfo.FilePaths.Length > 0)
        {
            var folder = Path.GetDirectoryName(localInfo.FilePaths[0]);
            if (!string.IsNullOrEmpty(folder))
            {
                return new MenuItem(Strings.OpenFolder, () => Sys.OpenFolderInFileManager(folder));
            }
        }

        return null;
    }

    private static bool HasUrl(string str, out string? url)
    {
        url = null;
        try
        {
            var match = StringHelper.UrlRegex().Match(str);
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
