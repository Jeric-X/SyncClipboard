using Avalonia.Input.Platform;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.ClipboardAva;

internal partial class ClipboardFactory : ClipboardFactoryBase
{
    protected override ILogger Logger { get; set; }
    protected override IServiceProvider ServiceProvider { get; set; }
    protected override IWebDav WebDav { get; set; }

    private IClipboard Clipboard { get; } = App.Current.Clipboard;

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
        ClipboardMetaInfomation meta = new();
        var clipboard = App.Current.MainWindow.Clipboard!;

        for (int i = 0; i < 5; i++)
        {
            await _semaphoreSlim.WaitAsync(ctk);
            try
            {
                if (OperatingSystem.IsLinux())
                {
                    return await HandleLinuxClipboard(ctk);
                }
                else if (OperatingSystem.IsMacOS())
                {
                    return await HandleMacosClipboard(ctk);
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
            finally { _semaphoreSlim.Release(); }
            await Task.Delay(200, ctk);
        }

        return meta;
    }
}
