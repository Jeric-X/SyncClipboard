using System;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using SyncClipboard.Core.Interfaces;

namespace SyncClipboard.Desktop;

internal sealed class PlatformApplication : IPlatformApplication
{
    public void Exit()
    {
        if (App.Current.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime applicationLifetime)
        {
            throw new InvalidOperationException("The application lifetime does not support shutdown.");
        }

        applicationLifetime.Shutdown();
    }

    public void SetFont(string font)
    {
        if (string.IsNullOrEmpty(font))
        {
            App.Current.Resources["ProgramFont"] = App.Current.Resources["DefaultFont"]!;
        }
        else
        {
            App.Current.Resources["ProgramFont"] = new FontFamily(font);
        }
    }
}
