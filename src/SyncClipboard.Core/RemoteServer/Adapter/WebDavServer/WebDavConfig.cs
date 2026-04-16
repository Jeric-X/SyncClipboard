using SyncClipboard.Core.Attributes;
using SyncClipboard.Core.Utilities;
using System.Text.Json.Serialization;

namespace SyncClipboard.Core.RemoteServer.Adapter.WebDavServer;

[AccountConfigType(ConfigTypeName, Priority = 2)]
public record WebDavConfig : IAdapterConfig<WebDavConfig>
{
    public const string ConfigTypeName = "WebDAV";

    [PropertyDisplay("ServerAddress", Description = "WebDAVServerAddressDescription")]
    public string RemoteURL { get; set; } = string.Empty;

    [UserName]
    [PropertyDisplay("UserName")]
    public string UserName { get; set; } = string.Empty;

    [PropertyDisplay("Password", IsPassword = true)]
    public string Password { get; set; } = string.Empty;

    [PropertyDisplay("DeleteServerTemporaryFileAutoly", Description = "DeletePreviousFilesDescription")]
    public bool DeletePreviousFilesOnPush { get; set; } = true;

    public string CustomName { get; set; } = string.Empty;

    [JsonIgnore]
    public string NameSuggestion => $"{UserName} - {StringHelper.GetHostFromUrl(RemoteURL)} - {ConfigTypeName}";
}