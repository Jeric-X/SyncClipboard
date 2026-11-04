using Avalonia.Controls;
using System;

namespace SyncClipboard.Desktop.Controls;

/// <summary>Displays mixed selection but gives the next click an explicit add/remove intent.</summary>
public class HistoryTagCheckBox : CheckBox
{
    protected override Type StyleKeyOverride => typeof(CheckBox);

    protected override void Toggle() => SetCurrentValue(IsCheckedProperty, IsChecked != true);
}
