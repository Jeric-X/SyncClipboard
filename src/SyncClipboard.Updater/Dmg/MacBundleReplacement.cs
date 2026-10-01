using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Updater.Dmg;

internal sealed class MacBundleReplacement(string target, string backup, bool elevated)
{
    internal string Backup => backup;

    public async Task ApplyAsync(string source, IUpdateInteraction interaction, CancellationToken token)
    {
        interaction.SetRollbackAvailable(false);
        interaction.Report("backup", -1);
        await UpdateIo.RunAsync(UpdaterText.Current.BackUp + target, async () =>
        {
            PackageFiles.CheckSpace(Path.GetDirectoryName(backup)!, FileSystem.GetSize(target));
            await RemoveAsync(backup, token);
            await CopyAsync(target, backup, token);
        }, interaction.AskFailureActionAsync, token, backup);

        var modified = false;
        try
        {
            interaction.Report("installing", -1);
            await UpdateIo.RunAsync(UpdaterText.Current.Replace + target, async () =>
            {
                var existingBytes = Directory.Exists(target) ? FileSystem.GetSize(target) : 0;
                PackageFiles.CheckSpace(Path.GetDirectoryName(target)!, Math.Max(0, FileSystem.GetSize(source) - existingBytes));
                modified = true;
                interaction.SetRollbackAvailable(true);
                await RemoveAsync(target, token);
                await CopyAsync(source, target, token);
                await MacCommand.RunAsync("/usr/bin/codesign", ["--verify", "--deep", "--strict", target], token);
            }, interaction.AskFailureActionAsync, token, backup, canRollback: () => modified);
            interaction.Report("installing", 100);
        }
        catch (Exception original) when (original is not UpdateAbortedException && modified)
        {
            try
            {
                interaction.SetRollbackAvailable(false);
                interaction.Report("restoring", -1);
                await UpdateIo.RunAsync(UpdaterText.Current.Restore + target, async () =>
                {
                    await RemoveAsync(target, CancellationToken.None);
                    await CopyAsync(backup, target, CancellationToken.None);
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
        }
    }

    public Task CleanupAsync(IUpdateInteraction interaction)
        => UpdateIo.RunAsync(UpdaterText.Current.RemoveDirectory + backup,
            () => RemoveAsync(backup, CancellationToken.None), interaction.AskFailureActionAsync, CancellationToken.None, backup);

    private Task<string> RemoveAsync(string path, CancellationToken token)
        => MacCommand.RunAsync("/bin/rm", ["-rf", path], token, elevated);

    private Task<string> CopyAsync(string source, string destination, CancellationToken token)
        => MacCommand.RunAsync("/usr/bin/ditto", ["--noacl", source, destination], token, elevated);
}
