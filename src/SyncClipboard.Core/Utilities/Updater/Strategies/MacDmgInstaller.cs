using SyncClipboard.Core.Commons;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace SyncClipboard.Core.Utilities.Updater.Strategies;

internal sealed class MacDmgInstaller(UpdateTaskCoordinator coordinator) : IUpdateInstallStrategy
{
    public UpdatePackageKind Kind => UpdatePackageKind.MacBundle;

    public UpdateInstallCapability GetCapability()
    {
        var capability = UpdateFileSystem.GetLocationCapability(Kind, FindBundle(Env.ProgramPath));
        if (!capability.Supported) return capability;
        var target = capability.TargetPath;
        if ((target.StartsWith("/Volumes/", StringComparison.Ordinal) && !UpdateFileSystem.CanWrite(Path.GetDirectoryName(target)!))
            || target.Contains("/AppTranslocation/", StringComparison.Ordinal)
            || UpdateFileSystem.IsWithin(Env.AppDataDirectory, target)
            || File.Exists(Env.PortableUserConfigFile)
            || Directory.Exists(Env.PortableAppDataDirectory))
        {
            return new(UpdatePackageKind.Unsupported, target, I18n.Strings.UpdateLocationUnsupported);
        }
        return capability;
    }

    public Task<UpdateInstallTask> PrepareAsync(UpdateInstallRequest request, CancellationToken token)
        => coordinator.PrepareAsync(request, Path.GetDirectoryName(request.Capability.TargetPath)!, async (snapshot, work) =>
        {
            var target = snapshot.Capability.TargetPath;
            UpdateFileSystem.CheckSpace(work, UpdateFileSystem.GetSize(target));
            var stage = Path.Combine(work, "payload.app");
            await PrepareMacBundleAsync(snapshot, stage, work, token);
            await File.WriteAllTextAsync(Path.Combine(work, "install.terminal"), CreateTerminalProfile(work), token);
            var update = new UpdateInstallTask
            {
                Directory = work,
                Kind = Kind.ToString(),
                Target = target,
                Stage = stage,
                Backup = Path.Combine(work, "backup"),
                Executable = target,
                Version = snapshot.Version,
                Language = CultureInfo.CurrentUICulture.Name,
                ProcessId = Environment.ProcessId,
                Elevate = !UpdateFileSystem.CanWrite(Path.GetDirectoryName(target)!)
            };
            await PrepareWorkerAsync(update, token);
            return update;
        }, token, CanRemoveFailedPreparation);

    public Task StartAsync(UpdateInstallTask update, CancellationToken token)
        => UpdateTaskCoordinator.StartAsync(update, CreateWorkerStartInfo, token, launcherMayExit: true);

    private static bool CanRemoveFailedPreparation(string work)
        // A failed detach may leave a read-only DMG mounted here. Do not traverse it during cleanup.
        => !Directory.Exists(Path.Combine(work, "mount"));

    private static async Task PrepareWorkerAsync(UpdateInstallTask update, CancellationToken token)
    {
        foreach (var name in new[] { "InstallMacBundle.sh", "OpenMacUpdate.command", "ElevateMacUpdate.applescript" })
            await UpdateTaskCoordinator.ExtractScriptAsync(update.Directory, name, token);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.Combine(update.Directory, "OpenMacUpdate.command"),
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        // Shell reads separate UTF-8 values as data, never sources generated shell code.
        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["kind"] = update.Kind,
            ["language"] = update.Language,
            ["target"] = update.Target,
            ["stage"] = update.Stage,
            ["backup"] = update.Backup,
            ["executable"] = update.Executable,
            ["pid"] = update.ProcessId.ToString(),
            ["elevate"] = update.Elevate ? "yes" : "no"
        })
        {
            await File.WriteAllTextAsync(Path.Combine(update.Directory, key + ".txt"), value, token);
        }
    }

    internal static ProcessStartInfo CreateWorkerStartInfo(UpdateInstallTask update)
    {
        var start = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
        foreach (var argument in new[] { "-a", "Terminal", Path.Combine(update.Directory, "install.terminal") })
            start.ArgumentList.Add(argument);
        return start;
    }

    internal static string CreateTerminalProfile(string directory)
    {
        // exec preserves the installer's exit status instead of returning to an interactive shell.
        var command = "exec /bin/sh '" + Path.Combine(directory, "OpenMacUpdate.command").Replace("'", "'\"'\"'") + "'";
        return new XDocument(new XElement("plist", new XAttribute("version", "1.0"), new XElement("dict",
            new XElement("key", "name"), new XElement("string", "SyncClipboard Update"),
            new XElement("key", "type"), new XElement("string", "Window Settings"),
            new XElement("key", "CommandString"), new XElement("string", command),
            new XElement("key", "RunCommandAsShell"), new XElement("false"),
            // Terminal: close only when the command exits successfully; leave failures visible.
            new XElement("key", "shellExitAction"), new XElement("integer", 1)))).ToString();
    }

    private static string? FindBundle(string path)
    {
        for (var dir = new DirectoryInfo(Path.GetDirectoryName(path)!); dir is not null; dir = dir.Parent)
        {
            if (dir.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) return dir.FullName;
        }
        return null;
    }

    private static async Task PrepareMacBundleAsync(UpdateInstallRequest request, string stage, string work, CancellationToken token)
    {
        var mount = Path.Combine(work, "mount");
        Directory.CreateDirectory(mount);
        var attached = false;
        try
        {
            await RunAsync("/usr/bin/hdiutil", token, "attach", "-readonly", "-nobrowse", "-mountpoint", mount, request.PackagePath);
            attached = true;
            var bundle = Path.Combine(mount, "SyncClipboard.app");
            var plist = Path.Combine(bundle, "Contents", "Info.plist");
            var currentPlist = Path.Combine(request.Capability.TargetPath, "Contents", "Info.plist");
            var identity = await RunAsync("/usr/libexec/PlistBuddy", token, "-c", "Print :CFBundleIdentifier", plist);
            var currentIdentity = await RunAsync("/usr/libexec/PlistBuddy", token, "-c", "Print :CFBundleIdentifier", currentPlist);
            var version = await RunAsync("/usr/libexec/PlistBuddy", token, "-c", "Print :CFBundleShortVersionString", plist);
            var expectedVersion = request.Version.TrimStart('v').Split('-')[0];
            if (identity != "xyz.jericx.desktop.syncclipboard" || identity != currentIdentity || version != expectedVersion)
            {
                throw new InvalidDataException("The update bundle identity or version is invalid.");
            }
            var executableName = await RunAsync("/usr/libexec/PlistBuddy", token, "-c", "Print :CFBundleExecutable", plist);
            if (executableName != "SyncClipboard.Desktop.MacOS") throw new InvalidDataException("Unexpected bundle executable.");
            var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x86_64";
            await RunAsync("/usr/bin/lipo", token, Path.Combine(bundle, "Contents", "MacOS", executableName), "-verify_arch", arch);
            UpdatePackageVerifier.ValidatePackageInfo(Path.Combine(bundle, "Contents", "MonoBundle", Env.UpdateInfoFile), Path.GetFileName(request.PackagePath));
            await RunAsync("/usr/bin/codesign", token, "--verify", "--deep", "--strict", bundle);
            var requiredSpace = checked(UpdateFileSystem.GetSize(bundle) + UpdateFileSystem.GetSize(request.Capability.TargetPath));
            UpdateFileSystem.CheckSpace(work, requiredSpace);
            await RunAsync("/usr/bin/ditto", token, bundle, stage);
        }
        finally
        {
            // Detach even if attach/copy was interrupted. Never delete a still-mounted image recursively.
            try
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await RunAsync("/usr/bin/hdiutil", cleanup.Token, "detach", mount);
                Directory.Delete(mount);
            }
            catch when (!attached)
            {
                // A nonrecursive rmdir cannot remove an active mount point or a nonempty directory.
                // Reclaim an unused mount directory, but preserve the original attach error and any live mount.
                try { Directory.Delete(mount); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private static async Task<string> RunAsync(string executable, CancellationToken token, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Could not start " + executable);
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        var text = await output;
        var errorText = await error;
        if (process.ExitCode != 0) throw new IOException(executable + ": " + errorText);
        return text.Trim();
    }
}
