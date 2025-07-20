namespace SyncClipboard.Core.Models;

public record class OpenSourceSoftware(string Name, string HomePage, string LicensePath)
{
    public bool IsValidLicensePath => !string.IsNullOrWhiteSpace(LicensePath);
}