using FluentAvalonia.Core;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva;

internal class ClipboardFactory : ClipboardFactoryBase
{
    protected override ILogger Logger { get; set; }
    protected override IServiceProvider ServiceProvider { get; set; }
    protected override IWebDav WebDav { get; set; }

    private const string LOG_TAG = nameof(ClipboardFactory);

    public ClipboardFactory(IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
        Logger = ServiceProvider.GetRequiredService<ILogger>();
        WebDav = ServiceProvider.GetRequiredService<IWebDav>();
    }

    public override async Task<ClipboardMetaInfomation> GetMetaInfomation(CancellationToken ctk)
    {
        ClipboardMetaInfomation meta = new();
        var clipboard = App.Current.MainWindow.Clipboard!;

        for (int i = 0; i < 5; i++)
        {
            try
            {
                if (OperatingSystem.IsLinux())
                {
                    await HandleLinuxClipboard(meta, ctk);
                    if (meta.Text?.StartsWith('\ufffd') ?? false)
                    {
                        throw new Exception($"wrong clipboard with: \\ufffd");
                    }
                }
                else
                {
                    meta.Text = await clipboard?.GetTextAsync().WaitAsync(ctk)!;
                }
                break;
            }
            catch (Exception ex) when (ctk.IsCancellationRequested is false)
            {
                Logger.Write(ex.Message);
            }
            await Task.Delay(200, ctk);
        }

        return meta;
    }

    [SupportedOSPlatform("linux")]
    private static async Task HandleLinuxClipboard(ClipboardMetaInfomation meta, CancellationToken token)
    {
        var clipboard = App.Current.MainWindow.Clipboard!;
        var formats = await clipboard.GetFormatsAsync().WaitAsync(token);
        if (formats.Contains(Format.UriList))
        {
            var uriListbytes = await clipboard.GetDataAsync(Format.UriList).WaitAsync(token) as byte[];
            ArgumentNullException.ThrowIfNull(uriListbytes);
            var uriList = Encoding.UTF8.GetString(uriListbytes!);
            var uriArray = uriList.Split('\n');
            meta.Files = uriArray
                            .Select(x => x.Replace("\r", ""))
                            .Where(x => !string.IsNullOrEmpty(x))
                            .Select(x => new Uri(x).LocalPath).ToArray();
            return;
        }

        meta.Text = await clipboard?.GetTextAsync().WaitAsync(token)!;
    }
}
