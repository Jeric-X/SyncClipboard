namespace SyncClipboard.Updater;

internal static class UpdateRunner
{
    internal static async Task<int> RunAsync(string[] args, IUpdateInteraction interaction, CancellationToken token)
    {
        UpdateArguments? update = null;
        try
        {
            UpdaterText.Current = UpdaterText.FromArguments(args);
            update = UpdateArguments.Parse(args);
            return await UpdateWorker.RunAsync(update, interaction, token);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            await interaction.ShowResultAsync(new UpdateResult(1, error.Message, update?.WorkDirectory));
            return 1;
        }
    }
}
