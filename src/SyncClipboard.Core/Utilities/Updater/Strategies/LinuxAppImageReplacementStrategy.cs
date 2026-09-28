using SyncClipboard.Core.Commons;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SyncClipboard.Core.Utilities.Updater.Strategies;

internal sealed class LinuxAppImageReplacementStrategy : IFileReplacementStrategy
{
    public UpdatePackageKind Kind => UpdatePackageKind.AppImage;

    public UpdateInstallCapability GetCapability()
    {
        var capability = UpdateFileSystem.GetLocationCapability(Kind, Env.GetAppImageExecPath());
        if (capability.Supported && !UpdateFileSystem.CanWrite(Path.GetDirectoryName(capability.TargetPath)!))
        {
            return new(UpdatePackageKind.Unsupported, capability.TargetPath, I18n.Strings.UpdateDirectoryNotWritable);
        }
        return capability;
    }

    public string GetInstallationDirectory(string target) => Path.GetDirectoryName(target)!;

    public async Task<UpdateInstallTask> PreparePayloadAsync(UpdateInstallRequest snapshot, string work, CancellationToken token)
    {
        var target = snapshot.Capability.TargetPath;
        var stage = Path.Combine(work, "payload");
        var helperExecutable = Path.Combine(work, "SyncClipboard-helper.AppImage");
        UpdateFileSystem.CheckSpace(work, checked((UpdateFileSystem.GetSize(target) * 2) + new FileInfo(snapshot.PackagePath).Length));
        File.Copy(target, helperExecutable);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(helperExecutable,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.Copy(snapshot.PackagePath, stage);
        ValidateAppImage(stage);
        await ValidatePayloadVersionAsync(stage, work, snapshot.Version, token);
        return new UpdateInstallTask
        {
            Directory = work,
            Kind = Kind.ToString(),
            Target = target,
            Stage = stage,
            Backup = Path.Combine(work, "backup"),
            Executable = target,
            HelperExecutable = helperExecutable,
            Version = snapshot.Version,
            ProcessId = Environment.ProcessId,
            Elevate = !UpdateFileSystem.CanWrite(Path.GetDirectoryName(target)!)
        };
    }

    public ProcessStartInfo CreateWorkerStartInfo(UpdateInstallTask update)
    {
        var start = AppImageInstallWorker.CreateLaunchInfo(update.HelperExecutable
            ?? throw new IOException("The AppImage update helper is missing."));
        start.ArgumentList.Add("--install-update");
        start.ArgumentList.Add(Path.Combine(update.Directory, "task.json"));
        return start;
    }

    internal static async Task ValidatePayloadVersionAsync(string image, string work, string version, CancellationToken token)
    {
        var inspection = Path.Combine(work, "version-inspection");
        Directory.CreateDirectory(inspection);
        try
        {
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(image,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            // Type-2 extraction uses the verified image's runtime without FUSE or starting the application.
            await ExtractImageAsync(image, inspection, token);
            var assemblies = Directory.EnumerateFiles(Path.Combine(inspection, "squashfs-root"), "SyncClipboard.Shared.dll",
                new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }).ToArray();
            if (assemblies.Length != 1) throw new InvalidDataException("The AppImage application metadata is missing or ambiguous.");
            UpdatePackageVerifier.ValidateVersion(FileVersionInfo.GetVersionInfo(assemblies[0]).ProductVersion, version);
        }
        finally
        {
            Directory.Delete(inspection, true);
        }
    }

    private static async Task ExtractImageAsync(string image, string destination, CancellationToken token)
    {
        var start = new ProcessStartInfo(image)
        {
            WorkingDirectory = destination,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.Environment.Remove("APPIMAGE_EXTRACT_AND_RUN");
        start.ArgumentList.Add("--appimage-extract");
        using var process = Process.Start(start) ?? throw new IOException("Could not inspect the AppImage update.");
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        await output;
        var errorText = await error;
        if (process.ExitCode != 0) throw new IOException("Could not extract AppImage metadata: " + errorText);
    }

    private static void ValidateAppImage(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[20];
        stream.ReadExactly(header);
        var machine = BinaryPrimitives.ReadUInt16LittleEndian(header[18..]);
        var expected = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 183 : 62;
        if (!header[..4].SequenceEqual(new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F' })
            || header[4] != 2 || header[5] != 1 || header[8] != 'A' || header[9] != 'I' || header[10] != 2 || machine != expected)
        {
            throw new InvalidDataException("The update is not a compatible type-2 AppImage.");
        }
    }
}
