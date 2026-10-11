using SyncClipboard.Core.Models;
using System.Text.Json.Serialization;

namespace SyncClipboard.Core.Utilities.History.HistoryExport;

public sealed record HistoryExportRecord(
    string Type, string Hash, string Text, long Size,
    DateTime Timestamp, DateTime LastModified, DateTime LastAccessed,
    bool Starred, bool Pinned, string From, HistoryExportTransferData? TransferData = null)
{
    public bool HasTransferData => TransferData is not null;

    [JsonIgnore]
    public ProfileType ProfileType => Enum.Parse<ProfileType>(Type);

    [JsonIgnore]
    public string ProfileId => Profile.GetProfileId(ProfileType, Hash);

    [JsonIgnore]
    public string[] FilePaths { get; init; } = [];

    [JsonIgnore]
    public string? TransferDataFile { get; init; }

    [JsonIgnore]
    public string? TransferDataHash { get; init; }

    public static HistoryExportRecord FromRecord(HistoryRecord record) => new(
        record.Type.ToString(), record.Hash.ToUpperInvariant(), record.Text, record.Size,
        record.Timestamp.ToUniversalTime(), record.LastModified.ToUniversalTime(), record.LastAccessed.ToUniversalTime(),
        record.Stared, record.Pinned, record.From)
    {
        FilePaths = [.. record.FilePath],
        TransferDataFile = record.TransferDataFile,
        TransferDataHash = record.TransferDataHash?.ToUpperInvariant()
    };
}

public sealed record HistoryExportTransferData(string Name, string Path, long Size, string Sha256);
