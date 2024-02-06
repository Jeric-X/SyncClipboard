using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.Models.Keyboard;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class HotkeyViewer : UserControl
{
    public HotkeyViewer()
    {
        InitializeComponent();
    }

    public Hotkey Hotkey
    {
        get { return (Hotkey)GetValue(HotkeyProperty); }
        set { SetValue(HotkeyProperty, value); }
    }

    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
            nameof(Hotkey),
            typeof(Hotkey),
            typeof(HotkeyViewer),
            new PropertyMetadata(Hotkey.Nothing)
    );
}
