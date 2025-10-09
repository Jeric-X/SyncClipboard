using SyncClipboard.Core.Attributes;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.RemoteServer.Adapter.WebDavServer;

[AccountConfigType(ConfigTypeName)]
public record WebDavConfig : IAdapterConfig<WebDavConfig>
{
    public const string ConfigTypeName = "WebDAV";

    [PropertyDisplay("ServerAddress", Description = "ServerAddressDescription")]
    public string RemoteURL { get; set; } = string.Empty;

    [UserName]
    [PropertyDisplay("UserName", Description = "UserNameDescription")]
    public string UserName { get; set; } = string.Empty;

    [PropertyDisplay("Password", IsPassword = true, Description = "PasswordDescription")]
    public string Password { get; set; } = string.Empty;

    [PropertyDisplay("DeleteServerTemporaryFileAutoly", Description = "DeletePreviousFilesDescription")]
    public bool DeletePreviousFilesOnPush { get; set; } = true;

    public string DisplayIdentify => $"{UserName} - {StringHelper.GetHostFromUrl(RemoteURL)} - {ConfigTypeName}";
}