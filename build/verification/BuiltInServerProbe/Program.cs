using SyncClipboard.Server.Core;
using SyncClipboard.Shared;

// The Python wrapper owns this temporary root, loopback endpoint and credentials.
var root = Path.GetFullPath(Environment.GetEnvironmentVariable("SYNC_PROBE_ROOT")
    ?? throw new InvalidOperationException("Missing temporary probe root."));
if (!Path.GetFileName(root).StartsWith("syncclipboard-openapi-", StringComparison.Ordinal)
    || !File.Exists(Path.Combine(root, "probe-settings.json")))
    throw new InvalidOperationException("The probe requires its isolated configuration.");

var password = Environment.GetEnvironmentVariable("SYNCCLIPBOARD_PASSWORD")
    ?? throw new InvalidOperationException("Missing probe credentials.");
var parameters = new ServerPara(
    Port: 0, Path: root, UserName: "smoke", Password: password,
    EnableHttps: false, CertificatePemPath: "", CertificatePemKeyPath: "",
    EnableCustomConfigurationFile: true,
    CustomConfigurationFilePath: Path.Combine(root, "probe-settings.json"),
    DiagnoseMode: Environment.GetEnvironmentVariable("SYNC_PROBE_DIAGNOSE") == "true",
    MaxSavedHistoryCount: 1000, HistoryRetentionMinutes: 10080, Services: new UnusedServices());
await using var app = await Web.StartAsync(parameters);
await app.WaitForShutdownAsync();

sealed class UnusedServices : IServiceProvider
{
    public object? GetService(Type serviceType) =>
        throw new InvalidOperationException($"The HTTP server must not request desktop services: {serviceType}.");
}
