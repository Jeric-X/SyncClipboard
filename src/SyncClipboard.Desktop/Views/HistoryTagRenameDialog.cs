using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.ViewModels;
using System;

namespace SyncClipboard.Desktop.Views;

public class HistoryTagRenameDialog : FAContentDialog
{
    protected override Type StyleKeyOverride => typeof(FAContentDialog);

    public HistoryTagRenameDialog(HistoryTagRenameViewModel model)
    {
        Title = Strings.HistoryTagRename;
        PrimaryButtonText = Strings.Confirm;
        CloseButtonText = Strings.Cancel;
        var input = new TextBox { Text = model.NewName };
        input.TextChanged += (_, _) => model.NewName = input.Text ?? string.Empty;
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap };
        message.Bind(TextBlock.TextProperty, new Binding(nameof(model.Message)) { Source = model });
        Content = new StackPanel
        {
            MaxWidth = 340,
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = Strings.HistoryTagRenameDescription, TextWrapping = TextWrapping.Wrap },
                input,
                message
            }
        };
        Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            input.Focus();
            input.SelectAll();
        });
        PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            input.IsEnabled = false;
            try
            {
                args.Cancel = !await model.SaveAsync();
            }
            finally
            {
                input.IsEnabled = true;
                deferral.Complete();
            }
        };
    }
}
