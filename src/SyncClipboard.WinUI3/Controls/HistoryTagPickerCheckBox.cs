using Microsoft.UI.Xaml.Controls;
using System;

namespace SyncClipboard.WinUI3.Controls;

public sealed class HistoryTagPickerCheckBox : CheckBox
{
    public HistoryTagPickerCheckBox() => DefaultStyleKey = typeof(CheckBox);

    public event EventHandler? ToggleRequested;

    // The picker updates IsChecked through the binding once the tag has been saved.
    protected override void OnToggle() => ToggleRequested?.Invoke(this, EventArgs.Empty);
}
