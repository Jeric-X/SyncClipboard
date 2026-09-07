namespace SyncClipboard.Server.Core.Models;

public sealed record HistoryTransferData(
    string FilePath,
    string TransferDataHash);
