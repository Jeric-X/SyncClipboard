using System;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SyncClipboard.Core.Interfaces;

namespace SyncClipboard.Desktop.ClipboardAva.ClipboardReader;

[SupportedOSPlatform("linux")]
public class WlClipboardReader(ILogger logger) : LinuxCmdClipboardReader(logger, "wl-clipboard", "wl-paste", "-t")
{
    public override async Task<string[]?> GetFormatsAsync(CancellationToken token)
    {
        if (await CheckAndGetDataByParasAsync("-l", token) is not byte[] textBytes)
            return null;
        var formatsStr = Encoding.UTF8.GetString(textBytes);
        return formatsStr.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    public override async Task<string?> GetTextAsync(CancellationToken token)
    {
        return await GetDataAsync(Format.TextUtf8, token) as string;
    }
}
