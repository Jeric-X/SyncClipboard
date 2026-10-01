namespace SyncClipboard.Updater;

internal sealed class UpdateRollbackException(Exception inner) : IOException(UpdaterText.Current.RollbackRequested, inner);

internal static class UpdateIo
{
    internal static async Task RunAsync(string path, Func<Task> operation, UpdateFailureHandler? onFailure,
        CancellationToken token, string? backupPath = null, bool canRollback = false)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                await operation();
                return;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException
                && error is not (UpdateAbortedException or UpdateRollbackException))
            {
                if (onFailure is null)
                    throw;
                var action = await onFailure(path, error, canRollback, token);
                token.ThrowIfCancellationRequested();
                if (action == UpdateFailureAction.Retry)
                    continue;
                if (canRollback && action == UpdateFailureAction.Rollback)
                    throw new UpdateRollbackException(error);
                throw new UpdateAbortedException(backupPath, error);
            }
        }
    }
}
