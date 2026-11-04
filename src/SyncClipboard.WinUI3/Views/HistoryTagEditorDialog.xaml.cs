using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.ViewModels;
using System;
using System.Threading.Tasks;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class HistoryTagEditorDialog : ContentDialog
{
    private readonly HistoryTagEditorViewModel _editor;
    private readonly Func<Task> _save;

    public HistoryTagEditorDialog(HistoryTagEditorViewModel editor, Func<Task> save)
    {
        _editor = editor;
        _save = save;
        InitializeComponent();
        DataContext = editor;
    }

    private void OnTagClick(object sender, RoutedEventArgs _)
    {
        if (sender is CheckBox { DataContext: HistoryTagChoice choice })
        {
            // The one-way binding preserves the model's previous mixed state until this click.
            choice.IsChecked = choice.IsChecked != true;
        }
    }

    private async void OnPrimaryButtonClick(ContentDialog _, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            await _save.Invoke();
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            _editor.Error = ex is ArgumentException ? Strings.HistoryTagsInvalid : ex.Message;
        }
        finally
        {
            deferral.Complete();
        }
    }
}
