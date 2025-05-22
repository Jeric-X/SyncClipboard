using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva.ClipboardReader;

public class MultiSourceClipboardReader(IEnumerable<IClipboardReader> sources) : IClipboardReader
{
    private readonly IEnumerable<IClipboardReader> _sources = sources ?? throw new ArgumentNullException(nameof(sources));

    private async Task<T?> TryGetFromSourcesAsync<T>(Func<IClipboardReader, CancellationToken, Task<T?>> getter, CancellationToken token)
    {
        foreach (var source in _sources)
        {
            try
            {
                var result = await getter(source, token).ConfigureAwait(false);
                if (result != null)
                    return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Ignore and try next source
            }
        }
        return default;
    }

    public Task<object?> GetDataAsync(string format, CancellationToken token)
    {
        return TryGetFromSourcesAsync((s, t) => s.GetDataAsync(format, t), token);
    }

    public Task<string[]?> GetFormatsAsync(CancellationToken token)
    {
        return TryGetFromSourcesAsync((s, t) => s.GetFormatsAsync(t), token);
    }

    public Task<string?> GetTextAsync(CancellationToken token)
    {
        return TryGetFromSourcesAsync((s, t) => s.GetTextAsync(t), token);
    }

    [SupportedOSPlatform("linux")]
    public Task<int?> GetTimeStamp(CancellationToken token)
    {
        return TryGetFromSourcesAsync((s, t) => s.GetTimeStamp(t), token);
    }
}
