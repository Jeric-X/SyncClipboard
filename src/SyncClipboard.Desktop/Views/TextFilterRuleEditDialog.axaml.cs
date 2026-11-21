using FluentAvalonia.UI.Controls;
using System;

namespace SyncClipboard.Desktop.Views;

public partial class TextFilterRuleEditDialog : FAContentDialog
{
    protected override Type StyleKeyOverride => typeof(FAContentDialog);

    public TextFilterRuleEditDialog() => InitializeComponent();
}
