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
        SizeChanged += (_, args) =>
        {
            foreach (ToggleButton button in _Keys.Children)
                button.MinWidth = args.NewSize.Height;
        };
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
            new PropertyMetadata(Hotkey.Nothing, (sender, _) => ((HotkeyViewer)sender).UpdateKeys())
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

    private void UpdateKeys()
    {
        var keys = Hotkey.Keys;
        while (_Keys.Children.Count > keys.Length)
            _Keys.Children.RemoveAt(_Keys.Children.Count - 1);

        // A hotkey has only a few keys. Keep them in a plain panel so scrolling the
        // settings list does not run another virtualizing layout for every row.
        for (var i = 0; i < keys.Length; i++)
        {
            if (i == _Keys.Children.Count)
                _Keys.Children.Add(CreateKeyButton());
            ((ToggleButton)_Keys.Children[i]).DataContext = keys[i];
        }
    }

    private ToggleButton CreateKeyButton()
    {
        var button = (ToggleButton)((DataTemplate)Resources["KeyTemplate"]).LoadContent();
        button.MinWidth = ActualHeight;
        button.SetBinding(FontSizeProperty, new Binding
        {
            Source = this,
            Path = new PropertyPath(nameof(FontSize)),
            Mode = BindingMode.OneWay
        });

        button.Checked += (_, _) => button.ClearValue(BorderThicknessProperty);
        button.Unchecked += (_, _) => button.BorderThickness = new Thickness(2);
        button.SetBinding(ToggleButton.IsCheckedProperty, new Binding
        {
            Source = this,
            Path = new PropertyPath(nameof(IsError)),
            Converter = new BoolNegationConverter(),
            Mode = BindingMode.OneWay
        });
        return button;
    }
}
