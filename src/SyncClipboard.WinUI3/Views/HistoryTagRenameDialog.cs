using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.WinUI3.Views;

public sealed class HistoryTagRenameDialog : ContentDialog
{
    public HistoryTagRenameDialog(HistoryTagRenameViewModel model)
    {
        Title = Strings.HistoryTagRename;
        PrimaryButtonText = Strings.Confirm;
        CloseButtonText = Strings.Cancel;
        var input = new TextBox { Text = model.NewName };
        input.TextChanged += (_, _) => model.NewName = input.Text;
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap };
        message.SetBinding(TextBlock.TextProperty, new Binding { Path = new PropertyPath(nameof(model.Message)), Source = model });
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
        Opened += (_, _) =>
        {
            input.Focus(FocusState.Programmatic);
            input.SelectAll();
        };
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
