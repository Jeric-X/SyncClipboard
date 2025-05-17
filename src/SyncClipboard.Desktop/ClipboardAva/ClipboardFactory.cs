using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva;

internal partial class ClipboardFactory : ClipboardFactoryBase
{
    protected override ILogger Logger { get; set; }
    protected override IServiceProvider ServiceProvider { get; set; }
    protected override IWebDav WebDav { get; set; }

    private static IClipboard Clipboard => App.Current.Clipboard;

    private const string LOG_TAG = nameof(ClipboardFactory);
    public static readonly SemaphoreSlim _semaphoreSlim = new(1, 1);

    public ClipboardFactory(IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
        Logger = ServiceProvider.GetRequiredService<ILogger>();
        WebDav = ServiceProvider.GetRequiredService<IWebDav>();
    }

    public override async Task<ClipboardMetaInfomation> GetMetaInfomation(CancellationToken ctk)
    {
        var clipboard = App.Current.MainWindow.Clipboard!;
        const int MAX_RETRY_TIMES = 5;

        for (int i = 0; i < MAX_RETRY_TIMES; i++)
        {
            await _semaphoreSlim.WaitAsync(ctk);
            try
            {
                var formats = await Clipboard.GetFormatsAsync().WaitAsync(ctk);
                if (formats is null)
                {
                    Logger.Write(LOG_TAG, $"GetFormatsAsync() is null");
                }
                else if (OperatingSystem.IsLinux())
                {
                    return await HandleLinuxClipboard(formats, ctk);
                }
                else if (OperatingSystem.IsMacOS())
                {
                    return await HandleMacosClipboard(formats, ctk);
                }
                else
                {
                    return new ClipboardMetaInfomation { Text = await clipboard?.GetTextAsync().WaitAsync(ctk)! };
                }
            }
            catch (Exception ex) when (ctk.IsCancellationRequested is false)
            {
                Logger.Write(ex.Message);
            }
            finally { _semaphoreSlim.Release(); }
            await Task.Delay(200, ctk);
        }

        throw new Exception("Can't get clipboard data");
    }

    private static async Task HandleFiles(ClipboardMetaInfomation meta, CancellationToken token)
    {
        var items = await Clipboard.GetDataAsync(Format.FileList).WaitAsync(token) as IEnumerable<IStorageItem>;
        meta.Files = items?.Select(item => item.Path.LocalPath).ToArray();
    }
}
