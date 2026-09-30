namespace SyncClipboard.Core.Interfaces;

public record UpdateInstallCapability(bool Supported, string? Reason = null);

public record UpdateInstallRequest(string PackagePath, string Digest, string Version);

public interface IUpdateInstaller
{
    // True when the caller must exit after StartAsync launches the updater successfully.
    bool RequiresAppExit { get; }
    UpdateInstallCapability GetCapability();
    Task StartAsync(UpdateInstallRequest request, CancellationToken token);
}
