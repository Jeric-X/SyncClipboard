using System.Text.Json;
using SyncClipboard.Updater.Core;

namespace SyncClipboard.Updater;

// Exercises generated JSON metadata in the actual NativeAOT binary; never performs an installation.
internal static class SmokeChecks
{
    public static int CheckProtocol()
    {
        var request = new UpdateRequest
        {
            PackageKind = UpdatePackageKind.WindowsZip,
            TargetVersion = "9.0.0-beta1",
            PackagePath = Path.Combine(AppContext.BaseDirectory, "package.zip"),
            InstallationPath = AppContext.BaseDirectory,
            ParentProcessId = Environment.ProcessId,
            Language = "zh"
        };
        var json = JsonSerializer.Serialize(request, UpdateJsonContext.Default.UpdateRequest);
        var restored = JsonSerializer.Deserialize(json, UpdateJsonContext.Default.UpdateRequest);
        if (restored != request) throw new InvalidDataException("Update request roundtrip failed.");
        var result = new UpdateResult(request.TargetVersion, UpdateOutcome.Failed, "Unsupported package");
        json = JsonSerializer.Serialize(result, UpdateJsonContext.Default.UpdateResult);
        if (JsonSerializer.Deserialize(json, UpdateJsonContext.Default.UpdateResult) != result)
            throw new InvalidDataException("Update result roundtrip failed.");
        Console.WriteLine("SELF_TEST=PASS");
        return 0;
    }
}
