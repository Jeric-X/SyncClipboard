namespace SyncClipboard.Core.Interfaces;

public record UpdateInstallRequest(string PackagePath, string Digest);

public interface IUpdateInstaller
{
    Task StartAsync(UpdateInstallRequest request, CancellationToken token);
}
