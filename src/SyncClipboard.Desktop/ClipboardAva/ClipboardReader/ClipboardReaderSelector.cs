using System;
using System.Collections.Generic;
using System.Linq;
using SyncClipboard.Core.Models;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace SyncClipboard.Desktop.ClipboardAva.ClipboardReader;

public class ClipboardReaderSelector : IClipboardReader
{
    private readonly Dictionary<string, IClipboardReader> _sources;
    private ClipboardReadMethod _readMethod;

    public string SourceName => _readMethod switch
    {
        ClipboardReadMethod.Avalonia => "Avalonia",
        ClipboardReadMethod.XClip => "xclip",
        ClipboardReadMethod.WlClipboard => "wl-clipboard",
        _ => throw new InvalidOperationException($"Unknown clipboard reading method: {_readMethod}")
    };

    public ClipboardReaderSelector(IEnumerable<IClipboardReader> sources, ConfigManager config)
        : this(sources, config, OperatingSystem.IsLinux()) { }

    internal ClipboardReaderSelector(IEnumerable<IClipboardReader> sources, ConfigManager config, bool isLinux)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = sources.ToDictionary(source => source.SourceName);
        config.GetAndListenConfig<ClipboardFactoryConfig>(
            value => _readMethod = isLinux ? value.ReadMethod : ClipboardReadMethod.Avalonia);
    }

    private IClipboardReader GetReader()
    {
        var name = SourceName;
        return _sources.TryGetValue(name, out var source)
            ? source
            : throw new InvalidOperationException($"Clipboard reader '{name}' is unavailable.");
    }

    public Task<object?> GetDataAsync(string format, CancellationToken token)
    {
        return GetReader().GetDataAsync(format, token);
    }

    public Task<string[]?> GetFormatsAsync(CancellationToken token)
    {
        return GetReader().GetFormatsAsync(token);
    }

    public Task<string?> GetTextAsync(CancellationToken token)
    {
        return GetReader().GetTextAsync(token);
    }

    public Task<Bitmap?> GetBitmapAsync(CancellationToken token)
    {
        return GetReader().GetBitmapAsync(token);
    }

    public Task<IStorageItem[]?> GetFilesAsync(CancellationToken token)
    {
        return GetReader().GetFilesAsync(token);
    }

    [SupportedOSPlatform("linux")]
    public Task<int?> GetTimeStamp(CancellationToken token)
    {
        return GetReader().GetTimeStamp(token);
    }
}
