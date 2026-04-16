using SyncClipboard.Core.Attributes;
using SyncClipboard.Core.Utilities;
using System.Text.Json.Serialization;

namespace SyncClipboard.Core.RemoteServer.Adapter.S3Server;

[AccountConfigType(ConfigTypeName, Priority = 3)]
public record S3Config : IAdapterConfig<S3Config>
{
    public const string ConfigTypeName = "S3";

    [PropertyDisplay("ServerAddress", Description = "S3ServerAddressDescription")]
    public string ServiceURL { get; set; } = string.Empty;

    [PropertyDisplay("S3Region", Description = "S3RegionDescription")]
    public string Region { get; set; } = "us-east-1";

    [PropertyDisplay("S3BucketName", Description = "S3BucketNameDescription")]
    public string BucketName { get; set; } = string.Empty;

    [PropertyDisplay("S3ObjectPrefix", Description = "S3ObjectPrefixDescription")]
    public string ObjectPrefix { get; set; } = "syncclipboard";

    [PropertyDisplay("S3ForcePathStyle", Description = "S3ForcePathStyleDescription")]
    public bool ForcePathStyle { get; set; } = false;

    [UserName]
    [PropertyDisplay("S3AccessKeyId")]
    public string AccessKeyId { get; set; } = string.Empty;

    [PropertyDisplay("S3SecretAccessKey", IsPassword = true)]
    public string SecretAccessKey { get; set; } = string.Empty;

    [PropertyDisplay("DeleteServerTemporaryFileAutoly", Description = "DeletePreviousFilesDescription")]
    public bool DeletePreviousFilesOnPush { get; set; } = true;

    public string CustomName { get; set; } = string.Empty;

    [JsonIgnore]
    public string NameSuggestion
    {
        get
        {
            var endpoint = string.IsNullOrWhiteSpace(ServiceURL)
                ? Region
                : StringHelper.GetHostFromUrl(ServiceURL);
            return $"{AccessKeyId} - {endpoint} - {ConfigTypeName}";
        }
    }
}
