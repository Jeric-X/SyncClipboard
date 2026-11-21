using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class FilterRuleEditDialog : ContentDialog
{
    public FilterRuleEditor Editor { get; }

    public FilterRuleEditDialog(FilterRuleEditor editor)
    {
        Editor = editor;
        InitializeComponent();
        Title = Strings.EditRule;
    }

    private void RegularExpressionQuickReference_Click(object _, RoutedEventArgs __)
    {
        Sys.OpenWithDefaultApp(Strings.RegularExpressionQuickReferenceLink);
    }
}
