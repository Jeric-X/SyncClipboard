
namespace SyncClipboard.Shared.Profiles;

public sealed class GroupEntry(string entryName, bool isDirectory, long length, Task<string>? hashTask)
{
    public string EntryName { get; } = entryName;
    public bool IsDirectory { get; } = isDirectory;
    public long Length { get; } = length;
    public Task<string>? HashTask { get; } = hashTask;

    public async Task<string> ToEntryStringAsync()
    {
        if (IsDirectory)
            return EntryName;

        var contentHash = HashTask is null ? string.Empty : await HashTask.ConfigureAwait(false);
        return $"{EntryName}|{Length}|{contentHash.ToUpperInvariant()}";
    }
}