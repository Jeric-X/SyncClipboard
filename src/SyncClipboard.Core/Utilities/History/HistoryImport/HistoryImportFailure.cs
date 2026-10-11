namespace SyncClipboard.Core.Utilities.History.HistoryImport;

public sealed record HistoryImportFailure(int Index, string ProfileId, string Reason);
