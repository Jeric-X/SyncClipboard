using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.Styling;
using FluentAvalonia.UI.Controls;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Desktop.Views;

namespace SyncClipboard.Test.Desktop;

[TestClass]
[DoNotParallelize]
public class HotkeyViewerTests
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions());

    [TestMethod]
    public async Task FirstLayoutInExpander_ShowsEveryKey_AndUpdatesWithoutNavigation()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HotkeyViewerTests));
        await session.Dispatch(() =>
        {
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            var viewer = new HotkeyViewer { Hotkey = new Hotkey(Key.Ctrl, Key.Shift, Key.V) };
            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { viewer, new Button { Content = "Edit" } }
            };
            var expander = new FASettingsExpander { Header = "System", IsExpanded = false };
            expander.Items.Add(new FASettingsExpanderItem { Content = "Show history", Footer = footer });
            expander.Loaded += (_, _) => expander.IsExpanded = true;
            var window = new Window
            {
                Width = 900,
                Height = 600,
                Content = new ScrollViewer { Content = expander }
            };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                AssertDisplayedKeys(viewer, "Ctrl", "Shift", "V");

                viewer.Hotkey = Hotkey.Nothing;
                window.UpdateLayout();
                AssertDisplayedKeys(viewer);

                viewer.Hotkey = new Hotkey(Key.Shift, Key.F1);
                window.UpdateLayout();
                AssertDisplayedKeys(viewer, "Shift", "F1");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static void AssertDisplayedKeys(HotkeyViewer viewer, params string[] expected)
    {
        var keys = viewer.GetVisualDescendants().OfType<ToggleButton>().ToArray();
        CollectionAssert.AreEqual(expected, keys.Select(key => key.Content?.ToString()).ToArray());
        Assert.IsTrue(keys.All(key => key.IsVisible && key.Bounds.Width > 0 && key.Bounds.Height > 0));
    }
}
