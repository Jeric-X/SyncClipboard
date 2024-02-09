using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
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

    // This is a workaround for https://github.com/microsoft/microsoft-ui-xaml/issues/560
    private void ToggleButton_Loaded(object sender, RoutedEventArgs _)
    {
        if (sender is ToggleButton toggleButton)
        {
            var binding = new Binding
            {
                RelativeSource = new RelativeSource { Mode = RelativeSourceMode.Self },
                Path = new PropertyPath("ActualHeight"),
                Mode = BindingMode.OneWay,
            };
            toggleButton.SetBinding(MinWidthProperty, binding);

            var fontSizeBinding = new Binding
            {
                Source = this,
                Path = new PropertyPath("FontSize"),
                Mode = BindingMode.OneWay,
            };
            toggleButton.SetBinding(FontSizeProperty, fontSizeBinding);
        }
    }
}