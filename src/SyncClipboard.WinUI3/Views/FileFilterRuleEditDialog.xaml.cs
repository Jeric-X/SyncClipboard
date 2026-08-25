using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.ViewModels;
using System.ComponentModel;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class FileFilterRuleEditDialog : ContentDialog
{
    public FileFilterRuleEditor Editor { get; }

    public FileFilterRuleEditDialog(FileFilterRuleEditor editor)
    {
        Editor = editor;
        InitializeComponent();
        Editor.PropertyChanged += EditorPropertyChanged;
        Closed += DialogClosed;
        UpdateValidation();
    }

    private void EditorPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateValidation();

    private void RegularExpressionQuickReference_Click(object _, Microsoft.UI.Xaml.RoutedEventArgs __)
    {
        Sys.OpenWithDefaultApp(Strings.RegularExpressionQuickReferenceLink);
    }

    private void UpdateValidation()
    {
        var message = FileSyncFilterSettingViewModel.ValidateRuleEditor(Editor);
        _ValidationMessage.Text = message;
        IsPrimaryButtonEnabled = string.IsNullOrEmpty(message);
    }

    private void DialogClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        Editor.PropertyChanged -= EditorPropertyChanged;
        Closed -= DialogClosed;
    }
}
