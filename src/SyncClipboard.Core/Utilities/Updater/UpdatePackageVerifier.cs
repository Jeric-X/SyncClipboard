using System.Security.Cryptography;
using System.Text.Json;

namespace SyncClipboard.Core.Utilities.Updater;

internal static class UpdatePackageVerifier
{
    public static async Task VerifyHashAsync(string path, string digest, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        var actual = "sha256:" + Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
        if (!string.Equals(actual, digest, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(I18n.Strings.HashMismatch);
        }
    }

    internal static void ValidateVersion(string? productVersion, string expectedVersion)
    {
        if (productVersion is null || !AppVersion.TryParse(productVersion.Split('+')[0], out var actual)
            || !AppVersion.TryParse(expectedVersion, out var expected) || actual.CompareTo(expected) != 0)
        {
            throw new InvalidDataException("The update package version does not match the release.");
        }
    }

    internal static void ValidatePackageInfo(string path, string packageName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.GetProperty("UpdateInfo").GetProperty("package_name").GetString() != packageName)
        {
            throw new InvalidDataException("The update package does not match this installation.");
        }
    }
}
