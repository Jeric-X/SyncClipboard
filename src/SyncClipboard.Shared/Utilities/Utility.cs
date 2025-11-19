using System.Security.Cryptography;

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
}
