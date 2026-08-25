using FluentAvalonia.UI.Controls;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Shared.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace SyncClipboard.Desktop.Views;

public partial class FileFilterRuleEditDialog : ContentDialog
{
    protected override Type StyleKeyOverride => typeof(ContentDialog);

    public FileFilterRuleEditor Editor { get; }
    public IReadOnlyList<LocaleString<FileFilterMatchMode>> MatchModes => FileSyncFilterSettingViewModel.MatchModes;

    public FileFilterRuleEditDialog()
        : this(FileSyncFilterSettingViewModel.CreateRuleEditor())
    {
    }

    public FileFilterRuleEditDialog(FileFilterRuleEditor editor)
    {
        Editor = editor;
        DataContext = this;
        InitializeComponent();
        Editor.PropertyChanged += EditorPropertyChanged;
        Closed += DialogClosed;
        UpdateValidation();
    }

    private void EditorPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateValidation();

    private void RegularExpressionQuickReference_Click(object? _, Avalonia.Interactivity.RoutedEventArgs __)
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
