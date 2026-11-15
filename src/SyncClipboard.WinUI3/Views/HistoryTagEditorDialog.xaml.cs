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

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("BackgroundElement") is FrameworkElement background)
            background.HorizontalAlignment = HorizontalAlignment.Stretch;
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

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (args.Result != ContentDialogResult.None || !_editor.HasChanges)
            return;

        args.Cancel = true;
        var confirm = ReferenceEquals(Content, _EditorContent);
        Content = confirm ? new TextBlock { Text = Strings.HistoryTagsDiscardMessage, TextWrapping = TextWrapping.Wrap } : _EditorContent;
        Title = confirm ? Strings.HistoryTagBarDiscardTitle : _editor.Title;
        PrimaryButtonText = confirm ? string.Empty : Strings.HistoryTagsApply;
        SecondaryButtonText = confirm ? Strings.HistoryTagBarDiscard : string.Empty;
        CloseButtonText = confirm ? Strings.HistoryTagBarContinueEditing : Strings.Cancel;
        DefaultButton = confirm ? ContentDialogButton.Close : ContentDialogButton.Primary;
        if (!confirm)
        {
            UpdateLayout();
            _TagSearch.Focus(FocusState.Programmatic);
        }
    }
}
