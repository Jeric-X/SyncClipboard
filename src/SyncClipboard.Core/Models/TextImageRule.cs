using System.Text.Json.Serialization;

namespace SyncClipboard.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TextImageRule
{
    Text,
    Image
}
