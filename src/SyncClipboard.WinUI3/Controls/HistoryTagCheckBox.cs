using Microsoft.UI.Xaml.Controls;

namespace SyncClipboard.WinUI3.Controls;

/// <summary>Displays mixed selection but gives the next click an explicit add/remove intent.</summary>
public sealed class HistoryTagCheckBox : CheckBox
{
    public HistoryTagCheckBox()
    {
        DefaultStyleKey = typeof(CheckBox);
    }

    protected override void OnToggle() => IsChecked = IsChecked != true;
}
