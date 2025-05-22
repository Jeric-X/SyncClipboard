using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using Avalonia.Controls;
using SyncClipboard.Core.Interfaces;
using System.Runtime.Versioning;
using System.Text;

namespace SyncClipboard.Desktop.ClipboardAva.ClipboardReader;

public class AvaloniaClipboardReader(IMainWindow mainWindow) : IClipboardReader
{
    private readonly IClipboard _clipboard = (mainWindow as Window)?.Clipboard ?? throw new ArgumentNullException(nameof(mainWindow));

    public async Task<string[]?> GetFormatsAsync(CancellationToken token)
    {
        return await _clipboard.GetFormatsAsync().WaitAsync(token);
    }

    public Task<string?> GetTextAsync(CancellationToken token)
    {
        return _clipboard.GetTextAsync().WaitAsync(token);
    }

    public async Task<object?> GetDataAsync(string format, CancellationToken token)
    {
        if (OperatingSystem.IsLinux())
        {
            if (format == Format.Targets)
            {
                return await GetFormatsAsync(token);
            }
            else if (format == Format.TimeStamp)
            {
                return await GetTimeStamp(token);
            }
        }
        return await _clipboard.GetDataAsync(format).WaitAsync(token);
    }

    [SupportedOSPlatform("linux")]
    public async Task<int?> GetTimeStamp(CancellationToken token)
    {
        if (await _clipboard.GetDataAsync(Format.TimeStamp).WaitAsync(token) is not byte[] bytes)
            return null;
        var str = Encoding.UTF8.GetString(bytes);
        bool canParse = int.TryParse(str, out var result);
        return canParse ? result : BitConverter.ToInt32(bytes);
    }
}