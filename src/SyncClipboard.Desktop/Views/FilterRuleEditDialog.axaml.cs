using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.ViewModels;
using System;

namespace SyncClipboard.Desktop.Views;

public partial class FilterRuleEditDialog : FAContentDialog
{
    protected override Type StyleKeyOverride => typeof(FAContentDialog);

    public FilterRuleEditDialog() => InitializeComponent();

    public FilterRuleEditDialog(FilterRuleEditor editor) : this() => DataContext = editor;

    private void RegularExpressionQuickReference_Click(object? _, RoutedEventArgs __)
    {
        Sys.OpenWithDefaultApp(Strings.RegularExpressionQuickReferenceLink);
    }
}
