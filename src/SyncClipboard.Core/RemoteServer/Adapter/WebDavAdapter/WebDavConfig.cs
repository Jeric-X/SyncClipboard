using SyncClipboard.Core.Commons;

namespace SyncClipboard.Core.RemoteServer.Adapter.WebDavAdapter;

[AccountConfig("WebDav")]
public record WebDavConfig : IAdapterConfig<WebDavConfig>
{
    public string RemoteURL { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool TrustInsecureCertificate { get; set; } = false;
    public uint TimeOut { get; set; } = 100;
    public bool DeletePreviousFilesOnPush { get; set; } = true;
}