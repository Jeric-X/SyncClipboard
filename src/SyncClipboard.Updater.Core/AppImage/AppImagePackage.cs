using SyncClipboard.Core.Utilities;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace SyncClipboard.Updater.AppImage;

internal static class AppImagePackage
{
    public static void ValidateTarget(UpdateArguments update)
    {
        if (!File.Exists(update.Target))
            throw new IOException(UpdaterText.Current.MissingAppImage);
        if (update.ProtectedPaths.Any(path => FileSystem.IsWithin(update.Target, path)))
            throw new IOException(UpdaterText.Current.ProtectedTarget);
    }

    public static async Task<string> PrepareAsync(UpdateArguments update, string attempt, CancellationToken token)
    {
        ValidateTarget(update);
        PackageFiles.CheckSpace(attempt, new FileInfo(update.PackagePath).Length);
        var snapshot = Path.Combine(attempt, "package.AppImage");
        await PackageFiles.CopyAsync(update.PackagePath, snapshot, token);
        await PackageFiles.VerifyHashAsync(snapshot, update.Digest, token);
        await ValidatePayloadAsync(snapshot, token);
        return snapshot;
    }

    internal static async Task ValidatePayloadAsync(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        var header = new byte[64];
        if (stream.Length < header.Length)
            throw new InvalidDataException(UpdaterText.Current.InvalidAppImage);
        await stream.ReadExactlyAsync(header, token);
        var machine = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 183 : 62;
        if (!header.AsSpan(0, 4).SequenceEqual(new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F' })
            || header[4] != 2 || header[5] != 1 || header[8] != 'A' || header[9] != 'I' || header[10] != 2
            || BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(18, 2)) != machine)
            throw new InvalidDataException(UpdaterText.Current.InvalidAppImage);
    }
}
