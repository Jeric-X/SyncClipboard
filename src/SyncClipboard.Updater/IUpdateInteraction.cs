namespace SyncClipboard.Updater;

internal sealed record UpdateResult(int ExitCode, string? Error = null, string? WorkDirectory = null, string? BackupPath = null);

internal interface IUpdateInteraction
{
    void Report(string phase, int percent);
    Task<bool> ConfirmForceExitAsync(CancellationToken token);
    Task<UpdateFailureAction> AskFailureActionAsync(string path, Exception error, CancellationToken token);
    Task ShowResultAsync(UpdateResult result);
}
