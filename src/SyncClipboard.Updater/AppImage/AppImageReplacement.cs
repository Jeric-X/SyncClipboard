namespace SyncClipboard.Updater.AppImage;

internal sealed class AppImageReplacement(string target, string backup, bool elevated)
{
    internal string Backup => backup;
    private const UnixFileMode ExecutableMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    public async Task ApplyAsync(string source, IUpdateInteraction interaction, CancellationToken token)
    {
        interaction.SetRollbackAvailable(false);
        interaction.Report("backup", -1);
        await UpdateIo.RunAsync(UpdaterText.Current.BackUp + target, async () =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            PackageFiles.CheckSpace(Path.GetDirectoryName(backup)!, new FileInfo(target).Length);
            File.Delete(backup);
            await CopyAsync(target, backup, token);
        }, interaction.AskFailureActionAsync, token, backup);
        var modified = false;
        try
        {
            interaction.Report("installing", -1);
            await UpdateIo.RunAsync(UpdaterText.Current.Replace + target, async () =>
            {
                var existingBytes = File.Exists(target) ? new FileInfo(target).Length : 0;
                PackageFiles.CheckSpace(Path.GetDirectoryName(target)!, Math.Max(0, new FileInfo(source).Length - existingBytes));
                modified = true;
                interaction.SetRollbackAvailable(true);
                await RemoveTargetAsync(token);
                await CopyAsync(source, target, token);
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
                    await RemoveTargetAsync(CancellationToken.None);
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
        => UpdateIo.RunAsync(UpdaterText.Current.RemoveTemporaryFile + backup, () =>
        {
            File.Delete(backup);
            return Task.CompletedTask;
        }, interaction.AskFailureActionAsync, CancellationToken.None, backup);

    private async Task RemoveTargetAsync(CancellationToken token)
    {
        if (elevated)
            await LinuxCommand.RunElevatedAsync("/bin/rm", ["-f", "--", target], token);
        else
            File.Delete(target);
    }

    private async Task CopyAsync(string source, string destination, CancellationToken token)
    {
        if (elevated)
        {
            await LinuxCommand.RunElevatedAsync("/bin/cp", ["--no-preserve=mode,ownership", "--", source, destination], token);
            await LinuxCommand.RunElevatedAsync("/bin/chmod", ["0755", "--", destination], token);
        }
        else
        {
            await PackageFiles.CopyAsync(source, destination, token);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(destination, ExecutableMode);
        }
    }
}
