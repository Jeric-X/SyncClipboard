using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System;

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

    public override ClipboardMetaInfomation GetMetaInfomation()
    {
        ClipboardMetaInfomation meta = new();
        var clipboard = App.Current.MainWindow.Clipboard;
        meta.Text = clipboard?.GetTextAsync().Result;

        return meta;
    }
}
