using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.ViewModels;
using System;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.Views;

public partial class HistoryTagEditorDialog : FAContentDialog
{
    private Button? closeButton;
    protected override Type StyleKeyOverride => typeof(FAContentDialog);

    public HistoryTagEditorDialog() => InitializeComponent();

    public HistoryTagEditorDialog(HistoryTagEditorViewModel viewModel, Func<Task> save) : this()
    {
        DataContext = viewModel;
        PrimaryButtonClick += async (_, args) =>
        {
            if (!ReferenceEquals(Content, _EditorContent))
            {
                args.Cancel = true;
                Dispatcher.UIThread.Post(() => Hide(FAContentDialogResult.Secondary));
                return;
            }

            var deferral = args.GetDeferral();
            try
            {
                await save();
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                viewModel.Error = ex is ArgumentException ? Strings.HistoryTagsInvalid : ex.Message;
            }
            finally
            {
                deferral.Complete();
            }
        };
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        closeButton = e.NameScope.Find<Button>("CloseButton");
    }

    protected override void OnOpened()
    {
        base.OnOpened();
        Dispatcher.UIThread.Post(() => _TagSearch.Focus(), DispatcherPriority.Input);
    }

    private void OnClosing(FAContentDialog _, FAContentDialogClosingEventArgs args)
    {
        if (args.Result != FAContentDialogResult.None || DataContext is not HistoryTagEditorViewModel editor || !editor.HasChanges)
            return;

        args.Cancel = true;
        var confirm = ReferenceEquals(Content, _EditorContent);
        Content = confirm ? new TextBlock { Text = Strings.HistoryTagsDiscardMessage, TextWrapping = TextWrapping.Wrap } : _EditorContent;
        Title = confirm ? Strings.HistoryTagBarDiscardTitle : editor.Title;
        PrimaryButtonText = confirm ? Strings.HistoryTagBarDiscard : Strings.HistoryTagsApply;
        CloseButtonText = confirm ? Strings.HistoryTagBarContinueEditing : Strings.Cancel;
        DefaultButton = confirm ? FAContentDialogButton.Close : FAContentDialogButton.None;
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(Content, _EditorContent))
                _TagSearch.Focus();
            else
                closeButton?.Focus();
        }, DispatcherPriority.Input);
    }
}
