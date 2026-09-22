using Avalonia.Input;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva.ClipboardWriter;

public class ClipboardWriterSelector : IClipboardWriter
{
    private readonly Dictionary<string, IClipboardWriter> _sources;
    private ClipboardWriteMethod _writeMethod;

    public bool SupportsMultipleFormats => GetWriter().SupportsMultipleFormats;

    public string SourceName => _writeMethod switch
    {
        ClipboardWriteMethod.Avalonia => "Avalonia",
        ClipboardWriteMethod.WlClipboard => "wl-clipboard",
        _ => throw new InvalidOperationException($"Unknown clipboard writing method: {_writeMethod}")
    };

    public ClipboardWriterSelector(IEnumerable<IClipboardWriter> sources, ConfigManager config)
        : this(sources, config, OperatingSystem.IsLinux()) { }

    internal ClipboardWriterSelector(IEnumerable<IClipboardWriter> sources, ConfigManager config, bool isLinux)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = sources.ToDictionary(source => source.SourceName);
        config.GetAndListenConfig<ClipboardFactoryConfig>(
            value => _writeMethod = isLinux ? value.WriteMethod : ClipboardWriteMethod.Avalonia);
    }

    private IClipboardWriter GetWriter()
    {
        var name = SourceName;
        return _sources.TryGetValue(name, out var source)
            ? source
            : throw new InvalidOperationException($"Clipboard writer '{name}' is unavailable.");
    }

    public Task SetTextAsync(string text, CancellationToken token) => GetWriter().SetTextAsync(text, token);

    public Task SetDataAsync(DataTransfer transfer, CancellationToken token) => GetWriter().SetDataAsync(transfer, token);
}
