using FluentAvalonia.UI.Controls;
using SyncClipboard.Core.ViewModels;
using System;

namespace SyncClipboard.Desktop.Views;

public partial class HistoryTagEditorDialog : FAContentDialog
{
    protected override Type StyleKeyOverride => typeof(FAContentDialog);

    public HistoryTagEditorDialog() => InitializeComponent();

    public HistoryTagEditorDialog(HistoryTagEditorViewModel viewModel) : this() => DataContext = viewModel;
}
