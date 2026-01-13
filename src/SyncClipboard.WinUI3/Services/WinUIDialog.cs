using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using System;
using System.Threading.Tasks;

namespace SyncClipboard.WinUI3.Services;

public class WinUIDialog : IMainWindowDialog
{
    private readonly XamlRoot? _xamlRoot;

    public WinUIDialog()
    {
    }

    public WinUIDialog(Window window)
    {
        _xamlRoot = window.Content.XamlRoot;
    }

    public async Task<bool> ShowConfirmationAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = Strings.Confirm,
            SecondaryButtonText = Strings.Cancel,
            DefaultButton = ContentDialogButton.Secondary,
            XamlRoot = _xamlRoot ?? App.Current.MainWindow.Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }
}