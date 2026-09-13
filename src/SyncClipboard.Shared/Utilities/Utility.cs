using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using SyncClipboard.Shared.Profiles;

namespace SyncClipboard.Shared.Utilities;

public static class Utility
{
    /// <summary>
    /// 仅包装未分类的本地读取异常；已分类的 Profile 数据异常和取消中的操作继续向外传播。
    /// </summary>
    public static bool ShouldWrapLocalReadFailure(Exception ex, CancellationToken token)
    {
        return ex is IOException or UnauthorizedAccessException &&
               ex is not LocalProfileDataUnavailableException &&
               !token.IsCancellationRequested;
    }

    /// <summary>
    /// 忽略大小写比较 SHA-256 字符串，不校验格式；两个 null 视为相同。
    /// </summary>
    public static bool SHA256Same(string? first, string? second)
    {
        return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsValidSHA256([NotNullWhen(true)] string? hash)
    {
        return hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
    }

    public static string? NormalizeSHA256(string? hash)
    {
        if (hash is null)
        {
            return null;
        }

        if (!IsValidSHA256(hash))
        {
            throw new ArgumentException("Hash must be a 64-character SHA-256 hex string.", nameof(hash));
        }

        return hash.ToUpperInvariant();
    }

    public static string NormalizeRequiredSHA256(string hash)
    {
        return NormalizeSHA256(hash) ?? throw new ArgumentException("SHA-256 hash cannot be empty.", nameof(hash));
    }

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

    /// <summary>
    /// 检查文件是否匹配指定的 SHA-256；缺少有效 hash、文件不可用或校验失败时返回 false，取消异常向外传播。
    /// </summary>
    public static async Task<bool> FileMatchesSHA256(string? path, string? expectedHash, CancellationToken token)
    {
        if (!IsValidSHA256(expectedHash) || string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            var actualHash = await CalculateFileSHA256(path, token);
            return SHA256Same(actualHash, expectedHash);
        }
        catch when (!token.IsCancellationRequested)
        {
            return false;
        }
    }

    public static async Task<string> VerifyFileSHA256(string path, string? expectedHash, CancellationToken token)
    {
        if (expectedHash is not null && !IsValidSHA256(expectedHash))
        {
            throw new InvalidDataException($"Invalid SHA-256 hash: {expectedHash}.");
        }

        var actualHash = await CalculateFileSHA256(path, token);
        if (expectedHash is not null && !SHA256Same(actualHash, expectedHash))
        {
            throw new InvalidDataException(
                $"File SHA-256 mismatch. Expected: {expectedHash}, Actual: {actualHash}.");
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
