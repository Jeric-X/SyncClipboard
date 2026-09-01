namespace SyncClipboard.Server.Core.Services.PushDevices;

public static class SyncDeviceIdentity
{
    public const string HeaderName = "X-SyncClipboard-Device-Id";

    public static string? Normalize(string? deviceId)
    {
        return Guid.TryParse(deviceId, out var parsedDeviceId)
            ? parsedDeviceId.ToString("D")
            : null;
    }
}
