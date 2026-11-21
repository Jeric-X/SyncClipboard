using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class TextFilterRuleEditDialog : ContentDialog
{
    public EditableTextFilterRule Editor { get; }

    public TextFilterRuleEditDialog(EditableTextFilterRule editor)
    {
        Editor = editor;
        InitializeComponent();
    }
}
