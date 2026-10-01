namespace SyncClipboard.Updater;

internal sealed record UpdateResult(int ExitCode, string? Error = null, string? WorkDirectory = null, string? BackupPath = null,
    bool CleanupIncomplete = false);

internal enum ForceExitAction { Yes, No, Retry }

internal delegate Task<UpdateFailureAction> UpdateFailureHandler(string path, Exception error, bool canRollback, CancellationToken token);

internal interface IUpdateInteraction
{
    void Report(string phase, int percent);
    void SetRollbackAvailable(bool available) { }
    Task<ForceExitAction> ConfirmForceExitAsync(CancellationToken token);
    Task<UpdateFailureAction> AskFailureActionAsync(string path, Exception error, bool canRollback, CancellationToken token);
    Task ShowResultAsync(UpdateResult result);
}
