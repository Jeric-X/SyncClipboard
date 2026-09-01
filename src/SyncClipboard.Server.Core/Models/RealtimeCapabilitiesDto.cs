using System.Text.Json.Serialization;

namespace SyncClipboard.Server.Core.Models;

public sealed class RealtimeCapabilitiesDto
{
    [JsonPropertyName("signalR")]
    public bool SignalR { get; init; }

    [JsonPropertyName("push")]
    public required PushCapabilitiesDto Push { get; init; }
}

public sealed class PushCapabilitiesDto
{
    [JsonPropertyName("fcm")]
    public bool Fcm { get; init; }
}
