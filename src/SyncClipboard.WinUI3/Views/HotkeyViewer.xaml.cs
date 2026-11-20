using CommunityToolkit.WinUI.Converters;
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

    public bool IsError
    {
        get { return (bool)GetValue(IsErrorProperty); }
        set { SetValue(IsErrorProperty, value); }
    }

    public static readonly DependencyProperty IsErrorProperty = DependencyProperty.Register(
            nameof(IsError),
            typeof(bool),
            typeof(HotkeyViewer),
            new PropertyMetadata(false)
    );

    // This is a workaround for https://github.com/microsoft/microsoft-ui-xaml/issues/560
    private void ToggleButton_Loaded(object sender, RoutedEventArgs _)
    {
        if (sender is ToggleButton toggleButton)
        {
            var widthBinding = new Binding
            {
                Source = this,
                Path = new PropertyPath("ActualHeight"),
                Mode = BindingMode.OneWay,
            };
            toggleButton.SetBinding(MinWidthProperty, widthBinding);

            var fontSizeBinding = new Binding
            {
                Source = this,
                Path = new PropertyPath("FontSize"),
                Mode = BindingMode.OneWay,
            };
            toggleButton.SetBinding(FontSizeProperty, fontSizeBinding);

            var isErrorBinding = new Binding
            {
                Source = this,
                Path = new PropertyPath(nameof(IsError)),
                Converter = new BoolNegationConverter(),
                Mode = BindingMode.OneWay
            };

            toggleButton.SetBinding(ToggleButton.IsCheckedProperty, isErrorBinding);
            toggleButton.Checked += delegate { toggleButton.ClearValue(BorderThicknessProperty); };
            toggleButton.Unchecked += delegate { toggleButton.SetValue(BorderThicknessProperty, new Thickness(2)); };
        }
    }
}