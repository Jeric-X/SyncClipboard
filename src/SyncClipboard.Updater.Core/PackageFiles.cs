using SyncClipboard.Core.Utilities;
using System.Security.Cryptography;

namespace SyncClipboard.Updater;

internal static class PackageFiles
{
    internal static async Task VerifyHashAsync(string path, string digest, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        var hash = "sha256:" + Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
        if (!string.Equals(hash, digest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(UpdaterText.Current.HashMismatch);
    }

    internal static async Task CopyAsync(string source, string destination, CancellationToken token)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, token);
        await output.FlushAsync(token);
    }

    internal static void CheckSpace(string directory, long bytes)
    {
        if (!FileSystem.HasEnoughSpace(directory, checked(bytes + (32L * 1024 * 1024))))
            throw new IOException(UpdaterText.Current.InsufficientSpace);
    }
}
