using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Desktop.ClipboardAva.ClipboardReader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva;

internal partial class ClipboardFactory : ClipboardFactoryBase
{
    private const int MAX_RETRY_TIMES = 5;
    protected override ILogger Logger { get; set; }
    protected override IServiceProvider ServiceProvider { get; set; }

    private readonly MultiSourceClipboardReader Clipboard;

    private const string LOG_TAG = nameof(ClipboardFactory);
    public static readonly SemaphoreSlim _semaphoreSlim = new(1, 1);

    public ClipboardFactory(IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
        Logger = ServiceProvider.GetRequiredService<ILogger>();
        Clipboard = ServiceProvider.GetRequiredService<MultiSourceClipboardReader>();
    }

    public override async Task<ClipboardMetaInfomation> GetMetaInfomation(CancellationToken ctk)
    {
        bool hasClipboard = false;

        for (int i = 0; i < MAX_RETRY_TIMES; i++)
        {
            await _semaphoreSlim.WaitAsync(ctk);
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    return new ClipboardMetaInfomation { Text = await Clipboard.GetTextAsync(ctk) };
                }

                var formats = await Clipboard.GetFormatsAsync(ctk);
                if (formats is null)
                {
                    await Logger.WriteAsync(LOG_TAG, $"GetFormatsAsync() is null");
                }
                else
                {
                    hasClipboard = true;
                    if (OperatingSystem.IsLinux())
                    {
                        return await HandleLinuxClipboard(formats, ctk);
                    }
                    else if (OperatingSystem.IsMacOS())
                    {
                        return await HandleMacosClipboard(formats, ctk);
                    }
                }
            }
            catch (Exception ex) when (ctk.IsCancellationRequested is false)
            {
                await Logger.WriteAsync(ex.Message);
            }
            finally { _semaphoreSlim.Release(); }
            await Task.Delay(200, ctk);
        }

        if (hasClipboard is false)
        {
            await Logger.WriteAsync(LOG_TAG, $"Clipboard is empty");
            return new ClipboardMetaInfomation();
        }
        throw new Exception("Can't get clipboard data");
    }

    private async Task HandleFiles(ClipboardMetaInfomation meta, CancellationToken token)
    {
        var items = await Clipboard.GetDataAsync(Format.FileList, token) as IEnumerable<IStorageItem>;
        meta.Files = items?.Select(item => item.Path.LocalPath).ToArray();
    }
}
