using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using System;
using System.Threading.Tasks;
using Microsoft.Windows.Storage.Pickers;

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

    public async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer
            {
                MaxHeight = 480,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap
                }
            },
            PrimaryButtonText = Strings.Confirm,
            XamlRoot = _xamlRoot ?? App.Current.MainWindow.Content.XamlRoot
        };

        await dialog.ShowAsync();
    }

    public async Task<bool?> ShowThreeButtonConfirmationAsync(string title, string message, string primaryText, string secondaryText, string closeText)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = primaryText,
            SecondaryButtonText = secondaryText,
            CloseButtonText = closeText,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _xamlRoot ?? App.Current.MainWindow.Content.XamlRoot
        };

        var result = await dialog.ShowAsync();

        return result switch
        {
            ContentDialogResult.Primary => true,
            ContentDialogResult.Secondary => false,
            _ => null
        };
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        try
        {
            var folderPicker = new FolderPicker(App.Current.MainWindow.AppWindow.Id)
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder,
                CommitButtonText = title
            };

            var folder = await folderPicker.PickSingleFolderAsync();
            return folder?.Path;
        }
        catch (Exception ex)
        {
            App.Current.Logger?.Write(nameof(WinUIDialog), ex.ToString());
            await ShowMessageAsync(title, ex.Message);
            return null;
        }
    }
}
