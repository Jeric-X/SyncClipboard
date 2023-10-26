using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System;
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
        var clipboard = App.Current.MainWindow.Clipboard;

        for (int i = 0; i < 5; i++)
        {
            try
            {
                var text = await clipboard?.GetTextAsync().WaitAsync(ctk)!;
                if (text?.StartsWith('\ufffd') ?? false)
                {
                    throw new Exception($"wrong clipboard with: {text}");
                }
                meta.Text = text;
                break;
            }
            catch (Exception ex) when (ctk.IsCancellationRequested is false)
            {
                Logger.Write(ex.Message);
            }
            await Task.Delay(200, ctk);
        }

        Logger.Write($"local: {meta.Text}");
        return meta;
    }
}
