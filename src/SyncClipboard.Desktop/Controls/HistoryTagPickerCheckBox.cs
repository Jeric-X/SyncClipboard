using Avalonia.Controls;
using System;

namespace SyncClipboard.Desktop.Controls;

public sealed class HistoryTagPickerCheckBox : CheckBox
{
    protected override Type StyleKeyOverride => typeof(CheckBox);

    public event EventHandler? ToggleRequested;

    // The picker updates IsChecked through the binding once the tag has been saved.
    protected override void Toggle() => ToggleRequested?.Invoke(this, EventArgs.Empty);
}
