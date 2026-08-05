using SyncClipboard.Core.Interfaces;
using SyncClipboard.Desktop.ClipboardAva.ClipboardReader;
using System;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva.Fingerprint;

/// <summary>
/// Linux 平台剪贴板指纹提供者（使用 TimeStamp）
/// </summary>
[SupportedOSPlatform("linux")]
internal class LinuxClipboardFingerprintProvider(
    MultiSourceClipboardReader clipboardReader,
    ILogger logger) : IClipboardFingerprintProvider
{
    private readonly MultiSourceClipboardReader _clipboardReader = clipboardReader;
    private readonly ILogger _logger = logger;

    public async Task<int?> GetClipboardFingerprint(CancellationToken ctk)
    {
        try
        {
            // On Wayland, retrieving the timestamp from a native Wayland clipboard owner can time out.
            // https://github.com/Jeric-X/SyncClipboard/issues/391
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ctk);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(100));

            var timeStampData = await _clipboardReader.GetDataAsync(Format.TimeStamp, timeout.Token);
            if (timeStampData is int timeStamp)
            {
                return timeStamp;
            }
            else if (timeStampData is byte[] bytes)
            {
                var str = Encoding.UTF8.GetString(bytes);
                return int.TryParse(str, out var result) ? result : BitConverter.ToInt32(bytes);
            }
            return null;
        }
        catch (OperationCanceledException) when (ctk.IsCancellationRequested is false)
        {
            return null;
        }
        catch (Exception ex)
        {
            await _logger.WriteAsync($"Failed to get clipboard fingerprint: {ex.Message}");
            return null;
        }
    }
}
