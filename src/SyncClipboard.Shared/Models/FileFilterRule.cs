using System.Text.Json.Serialization;

namespace SyncClipboard.Shared.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FileFilterMatchMode
{
    Suffix,
    Regex,
}

public record FileFilterRule
{
    public string Pattern { get; set; } = "";
    public FileFilterMatchMode MatchMode { get; set; } = FileFilterMatchMode.Suffix;
}
