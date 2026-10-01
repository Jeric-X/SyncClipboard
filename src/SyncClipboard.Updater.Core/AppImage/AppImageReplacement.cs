namespace SyncClipboard.Updater.AppImage;

internal sealed class AppImageReplacement(string target, string backup, bool elevated)
{
    internal string Backup => backup;
    private readonly string prepared = Path.Combine(Path.GetDirectoryName(target)!,
        ".SyncClipboard-update-" + Guid.NewGuid().ToString("N"));
    private const UnixFileMode ExecutableMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    public async Task ApplyAsync(string source, IUpdateInteraction interaction, CancellationToken token)
    {
        interaction.SetRollbackAvailable(false);
        interaction.Report("backup", -1);
        await InteractiveOperation.RunAsync(UpdaterText.Current.BackUp + target, async () =>
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
            await InteractiveOperation.RunAsync(UpdaterText.Current.Replace + target, async () =>
            {
                await PrepareAsync(source, token);
                await ReplaceAsync();
                modified = true;
                interaction.SetRollbackAvailable(true);
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
            await InteractiveOperation.RunAsync(UpdaterText.Current.RemoveTemporaryFile + prepared,
                () => RemovePreparedAsync(CancellationToken.None), interaction.AskFailureActionAsync,
                CancellationToken.None, backup);
        }
    }

    public Task CleanupAsync(IUpdateInteraction interaction)
        => InteractiveOperation.RunAsync(UpdaterText.Current.RemoveTemporaryFile + backup, () =>
        {
            File.Delete(backup);
            return Task.CompletedTask;
        }, interaction.AskFailureActionAsync, CancellationToken.None, backup);

    private async Task PrepareAsync(string source, CancellationToken token)
    {
        await RemovePreparedAsync(token);
        PackageFiles.CheckSpace(Path.GetDirectoryName(target)!, new FileInfo(source).Length);
        await CopyAsync(source, prepared, token);
    }

    private Task RemovePreparedAsync(CancellationToken token)
    {
        if (elevated)
            return LinuxCommand.RunElevatedAsync("/bin/rm", ["-f", "--", prepared], token);
        File.Delete(prepared);
        return Task.CompletedTask;
    }

    private Task ReplaceAsync()
    {
        if (elevated)
            return LinuxCommand.RunElevatedAsync("/bin/mv", ["-fT", "--", prepared, target], CancellationToken.None);
        File.Move(prepared, target, true);
        return Task.CompletedTask;
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
