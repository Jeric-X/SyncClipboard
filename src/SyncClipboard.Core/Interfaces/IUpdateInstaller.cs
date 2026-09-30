namespace SyncClipboard.Core.Interfaces;

public record UpdateInstallRequest(string PackagePath, string Digest, string Version);

public interface IUpdateInstaller
{
    Task StartAsync(UpdateInstallRequest request, CancellationToken token);
}
