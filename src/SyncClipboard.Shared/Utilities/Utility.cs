using System.Security.Cryptography;
using System.Text;

namespace SyncClipboard.Shared.Utilities;

public static class Utility
{
    public static async Task<string> CalculateSHA256(byte[] data, CancellationToken token)
    {
        using var ms = new MemoryStream(data);
        var hashBytes = await SHA256.HashDataAsync(ms, token);
        return Convert.ToHexString(hashBytes);
    }

    public static async Task<string> CalculateSHA256(Stream stream, CancellationToken token)
    {
        var hashBytes = await SHA256.HashDataAsync(stream, token);
        return Convert.ToHexString(hashBytes);
    }

    public static async Task<string> CalculateFileSHA256(string path, CancellationToken token)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, FileOptions.SequentialScan | FileOptions.Asynchronous);
        var hashBytes = await SHA256.HashDataAsync(file, token);
        return Convert.ToHexString(hashBytes);
    }

    public static async Task<string> VerifyFileSHA256(
        string path,
        string? expectedHash,
        CancellationToken token)
    {
        var actualHash = await CalculateFileSHA256(path, token);
        if (!string.IsNullOrEmpty(expectedHash) &&
            !string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Transfer data hash mismatch. Expected: {expectedHash}, Actual: {actualHash}.");
        }

        return actualHash;
    }

    public static Task<string> CalculateSHA256(string str, CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(str);
        return CalculateSHA256(bytes, token);
    }

    public static string CreateTimeBasedFileName()
    {
        return $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{Path.GetRandomFileName()}";
    }
}
