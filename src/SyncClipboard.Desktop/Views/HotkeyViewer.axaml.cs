using Avalonia;
using Avalonia.Controls;
using SyncClipboard.Core.Models.Keyboard;

namespace SyncClipboard.Desktop.Views;

public partial class HotkeyViewer : UserControl
{
    public HotkeyViewer()
    {
        InitializeComponent();
    }

    public Hotkey Hotkey
    {
        get { return GetValue(HotkeyProperty); }
        set { SetValue(HotkeyProperty, value); }
    }

    public static readonly StyledProperty<Hotkey> HotkeyProperty = AvaloniaProperty.Register<HotkeyViewer, Hotkey>(
        nameof(Hotkey),
        Hotkey.Nothing
    );

    public bool IsError
    {
        get { return GetValue(IsErrorProperty); }
        set { SetValue(IsErrorProperty, value); }
    }

    public static readonly StyledProperty<bool> IsErrorProperty = AvaloniaProperty.Register<HotkeyViewer, bool>(
        nameof(IsError),
        false
    );
}
