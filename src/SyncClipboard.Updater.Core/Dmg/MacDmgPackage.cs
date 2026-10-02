using SyncClipboard.Core.Utilities;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;

namespace SyncClipboard.Updater.Dmg;

internal sealed class MacDmgPackage(string bundlePath, string mountPath)
{
    public string BundlePath { get; } = bundlePath;

    public const string ExecutableName = "SyncClipboard.Desktop.MacOS";

    public static void ValidateTarget(UpdateArguments update)
    {
        if (!Directory.Exists(update.Target) || !update.Target.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(Path.Combine(update.Target, "Contents", "Info.plist")))
            throw new IOException(UpdaterText.Current.InvalidAppBundle);
        if (update.WorkDirectory is not null && FileSystem.IsWithin(update.WorkDirectory, update.Target))
            throw new IOException(UpdaterText.Current.WorkspaceInsideTarget);
        foreach (var path in update.ProtectedPaths)
        {
            if (FileSystem.IsWithin(update.Target, path))
                throw new IOException(UpdaterText.Current.ProtectedTarget);
            if (FileSystem.IsWithin(path, update.Target) && (File.Exists(path) || Directory.Exists(path)))
                throw new IOException(UpdaterText.Current.DataInsideBundle + path);
        }
    }

    public static async Task<MacDmgPackage> PrepareAsync(UpdateArguments update, string attempt, CancellationToken token,
        UpdateFailureHandler? onFailure = null)
    {
        ValidateTarget(update);
        PackageFiles.CheckSpace(attempt, new FileInfo(update.PackagePath).Length);
        var snapshot = Path.Combine(attempt, "package.dmg");
        await PackageFiles.CopyAsync(update.PackagePath, snapshot, token);
        await PackageFiles.VerifyHashAsync(snapshot, update.Digest, token);
        var mount = Path.Combine(attempt, "mount");
        Directory.CreateDirectory(mount);
        await MacCommand.RunAsync("/usr/bin/hdiutil", ["attach", "-readonly", "-nobrowse", "-mountpoint", mount, snapshot], token);
        var package = new MacDmgPackage(Path.Combine(mount, "SyncClipboard.app"), mount);
        try
        {
            await ValidatePayloadAsync(package.BundlePath, Path.GetFileName(update.PackagePath), token);
            return package;
        }
        catch
        {
            await package.DetachAsync(onFailure);
            throw;
        }
    }

    public Task DetachAsync(UpdateFailureHandler? onFailure)
        => InteractiveOperation.RunAsync(UpdaterText.Current.DetachImage + mountPath,
            () => MacCommand.RunAsync("/usr/bin/hdiutil", ["detach", mountPath], CancellationToken.None),
            onFailure, CancellationToken.None);

    internal static async Task DetachAttemptAsync(string attempt)
    {
        var mount = Path.Combine(attempt, "mount");
        if (!Directory.Exists(mount))
            return;
        var identity = await MacCommand.RunAsync("/usr/bin/stat", ["-f", "%d:%i", mount], CancellationToken.None);
        var info = await MacCommand.RunAsync("/usr/bin/hdiutil", ["info", "-plist"], CancellationToken.None);
        var mounts = XDocument.Parse(info).Descendants("key").Where(key => key.Value == "mount-point")
            .Select(key => (key.NextNode as XElement)?.Value);
        foreach (var mountedPath in mounts)
        {
            if (mountedPath is null || !Directory.Exists(mountedPath))
                continue;
            var mountedIdentity = await MacCommand.RunAsync("/usr/bin/stat", ["-f", "%d:%i", mountedPath], CancellationToken.None);
            if (mountedIdentity != identity)
                continue;
            await MacCommand.RunAsync("/usr/bin/hdiutil", ["detach", mountedPath], CancellationToken.None);
            return;
        }
    }

    internal static async Task ValidatePayloadAsync(string bundle, string packageName, CancellationToken token)
    {
        var contents = Path.Combine(bundle, "Contents");
        var executable = Path.Combine(contents, "MacOS", ExecutableName);
        if (!File.Exists(executable) || !File.Exists(Path.Combine(contents, "Info.plist")))
            throw new InvalidDataException(UpdaterText.Current.InvalidAppBundle);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(contents, "MonoBundle", "update_info.json")));
        var info = document.RootElement.GetProperty("UpdateInfo");
        if (info.GetProperty("manage_type").GetString() != "manual" || info.GetProperty("update_src").GetString() != "github"
            || info.GetProperty("package_name").GetString() != packageName)
            throw new InvalidDataException(UpdaterText.Current.PackageMismatch);
        var architecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x86_64";
        await MacCommand.RunAsync("/usr/bin/lipo", [executable, "-verify_arch", architecture], token);
        await MacCommand.RunAsync("/usr/bin/codesign", ["--verify", "--deep", "--strict", bundle], token);
    }
}
