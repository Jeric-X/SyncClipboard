using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Updater.Dmg;

internal sealed class MacBundleReplacement(string target, string backup, bool elevated)
{
    internal string Backup => backup;
    private readonly string prepared = Path.Combine(Path.GetDirectoryName(target)!,
        ".SyncClipboard-update-" + Guid.NewGuid().ToString("N") + ".app");

    public async Task ApplyAsync(string source, IUpdateInteraction interaction, CancellationToken token)
    {
        interaction.SetRollbackAvailable(false);
        interaction.Report("backup", -1);
        await InteractiveOperation.RunAsync(UpdaterText.Current.BackUp + target, async () =>
        {
            PackageFiles.CheckSpace(Path.GetDirectoryName(backup)!, FileSystem.GetSize(target));
            await RemoveAsync(backup, token);
            await CopyAsync(target, backup, token);
        }, interaction.AskFailureActionAsync, token, backup);

        var modified = false;
        try
        {
            interaction.Report("installing", -1);
            await InteractiveOperation.RunAsync(UpdaterText.Current.Replace + target, async () =>
            {
                await PrepareAsync(source, token);
                await MacCommand.RunAsync("/usr/bin/codesign", ["--verify", "--deep", "--strict", prepared], token);
                // The unsupported-filesystem fallback may remove the old bundle before a later rename fails.
                modified = true;
                interaction.SetRollbackAvailable(true);
                await ReplaceAsync();
            }, interaction.AskFailureActionAsync, token, backup, canRollback: () => modified);
            interaction.Report("installing", 100);
        }
        catch (Exception original) when (original is not UpdateAbortedException && modified)
        {
            try
            {
                interaction.SetRollbackAvailable(false);
                interaction.Report("restoring", -1);
                await InteractiveOperation.RunAsync(UpdaterText.Current.Restore + target, async () =>
                {
                    await PrepareAsync(backup, CancellationToken.None);
                    await ReplaceAsync();
                }, interaction.AskFailureActionAsync, CancellationToken.None, backup);
            }
            catch (Exception error)
            {
                throw new UpdateRecoveryException(backup, original, [error]);
            }
            throw new IOException(UpdaterText.Current.RolledBack, original);
        }
        finally
        {
            interaction.SetRollbackAvailable(false);
            await InteractiveOperation.RunAsync(UpdaterText.Current.RemoveDirectory + prepared,
                () => RemoveAsync(prepared, CancellationToken.None), interaction.AskFailureActionAsync,
                CancellationToken.None, backup);
        }
    }

    public Task CleanupAsync(IUpdateInteraction interaction)
        => InteractiveOperation.RunAsync(UpdaterText.Current.RemoveDirectory + backup,
            () => RemoveAsync(backup, CancellationToken.None), interaction.AskFailureActionAsync, CancellationToken.None, backup);

    private async Task PrepareAsync(string source, CancellationToken token)
    {
        await RemoveAsync(prepared, token);
        PackageFiles.CheckSpace(Path.GetDirectoryName(target)!, FileSystem.GetSize(source));
        await CopyAsync(source, prepared, token);
    }

    private Task ReplaceAsync()
    {
        if (elevated)
            return MacCommand.RunAsync(Environment.ProcessPath!, ["--replace-bundle", prepared, target], CancellationToken.None, true);
        MacBundleSwap.Replace(prepared, target);
        return Task.CompletedTask;
    }

    private Task<string> RemoveAsync(string path, CancellationToken token)
        => MacCommand.RunAsync("/bin/rm", ["-rf", path], token, elevated);

    private Task<string> CopyAsync(string source, string destination, CancellationToken token)
        => MacCommand.RunAsync("/usr/bin/ditto", ["--noacl", source, destination], token, elevated);
}
