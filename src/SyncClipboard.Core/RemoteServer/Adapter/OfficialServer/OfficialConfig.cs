using SyncClipboard.Core.Attributes;

namespace SyncClipboard.Core.RemoteServer.Adapter.OfficialServer;

[AccountConfigType(TYPE_NAME)]
public record OfficialConfig : IAdapterConfig<OfficialConfig>
{
    public const string TYPE_NAME = "SyncClipboard";

    [PropertyDisplay("ServerAddress")]
    public string RemoteURL { get; set; } = string.Empty;

    [UserName]
    [PropertyDisplay("UserName")]
    public string UserName { get; set; } = string.Empty;

    [PropertyDisplay("Password", IsPassword = true)]
    public string Password { get; set; } = string.Empty;

    [PropertyDisplay("DeleteServerTemporaryFileAutoly", Description = "DeletePreviousFilesDescription")]
    public bool DeletePreviousFilesOnPush { get; set; } = true;
}