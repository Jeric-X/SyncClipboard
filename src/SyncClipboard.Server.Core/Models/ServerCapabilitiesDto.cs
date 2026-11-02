using System.Text.Json.Serialization;

namespace SyncClipboard.Server.Core.Models;

public class ServerCapabilitiesDto
{
    [JsonPropertyName("historySync")]
    public bool HistorySync { get; init; }

    [JsonPropertyName("historyTags")]
    public bool HistoryTags { get; init; }
}
