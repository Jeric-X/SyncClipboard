using SyncClipboard.Updater;

namespace SyncClipboard.Test;

internal sealed class RecordingUpdateInteraction : IUpdateInteraction
{
    public UpdateResult? Result { get; private set; }
    public string? FailurePath { get; private set; }
    public void Report(string phase, int percent) { }
    public Task<ForceExitAction> ConfirmForceExitAsync(CancellationToken token)
        => throw new AssertFailedException("Unexpected force-exit confirmation.");
    public Task<UpdateFailureAction> AskFailureActionAsync(string path, Exception error, bool canRollback, CancellationToken token)
    {
        FailurePath = path;
        return Task.FromResult(UpdateFailureAction.Abort);
    }
    public Task ShowResultAsync(UpdateResult result)
    {
        Result = result;
        return Task.CompletedTask;
    }
}
