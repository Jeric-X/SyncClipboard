using SyncClipboard.Core.Commons;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace SyncClipboard.Core.Utilities.Updater;

internal sealed class LinuxAppImageInstaller(UpdateInstallHelper helper) : IUpdateInstallStrategy
{
    public UpdatePackageKind Kind => UpdatePackageKind.AppImage;

    public UpdateInstallCapability GetCapability()
    {
        var capability = UpdateInstallFiles.GetLocationCapability(Kind, Env.GetAppImageExecPath());
        if (capability.Supported && !UpdateInstallFiles.CanWrite(Path.GetDirectoryName(capability.TargetPath)!))
        {
            return new(UpdatePackageKind.Unsupported, capability.TargetPath, I18n.Strings.UpdateDirectoryNotWritable);
        }
        return capability;
    }

    public Task<PreparedUpdate> PrepareAsync(UpdateInstallRequest request, CancellationToken token)
        => helper.PrepareAsync(request, Path.GetDirectoryName(request.Capability.TargetPath)!, (snapshot, work) =>
        {
            var target = snapshot.Capability.TargetPath;
            var stage = Path.Combine(work, "payload");
            var helperExecutable = Path.Combine(work, "SyncClipboard-helper.AppImage");
            UpdateInstallFiles.CheckSpace(work, checked(UpdateInstallFiles.GetSize(target) * 2 + new FileInfo(snapshot.PackagePath).Length));
            File.Copy(target, helperExecutable);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(helperExecutable,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.Copy(snapshot.PackagePath, stage);
            ValidateAppImage(stage);
            return Task.FromResult(new PreparedUpdate
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
                Elevate = !UpdateInstallFiles.CanWrite(Path.GetDirectoryName(target)!)
            });
        }, token);

    public Task StartAsync(PreparedUpdate update, CancellationToken token) => UpdateInstallHelper.StartAsync(update, token);

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
