using System.Text.Json;

namespace SyncClipboard.Updater.Core;

// All artifacts stay next to the request, outside the future installation target.
// The main application's future integration owns cleanup; this framework never removes tasks.
public static class UpdateTaskFile
{
    public static async Task<UpdateRequest> ReadAsync(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        var request = await JsonSerializer.DeserializeAsync(stream, UpdateJsonContext.Default.UpdateRequest, token)
            ?? throw new InvalidDataException("The update request is empty.");
        // Validation belongs to UpdateRunner so invalid requests still produce a terminal result.
        return request;
    }

    public static async Task WriteResultAsync(string directory, UpdateResult result)
    {
        var temporary = Path.Combine(directory, "result-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await JsonSerializer.SerializeAsync(stream, result, UpdateJsonContext.Default.UpdateResult);
            // Preserve existing results, especially failed attempts. A retry needs a new task directory.
            File.Move(temporary, Path.Combine(directory, "result.json"));
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
