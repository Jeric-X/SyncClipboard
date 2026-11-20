using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Test.WinUI3.App;
using SyncClipboard.WinUI3.Views;
using Windows.Graphics;

namespace SyncClipboard.Test.WinUI3.Hotkeys;

[TestClass]
public class HotkeyViewerTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("PlatformWindows")]
    public async Task KeysAndAppearanceFollowEditsAndReloads()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("WinUI needs Windows.");
            return;
        }

        await UITestProcess.RunAsync(CheckInWindowAsync, TestContext.CancellationTokenSource.Token);
    }

    internal static async Task CheckInWindowAsync()
    {
        var viewer = new HotkeyViewer
        {
            Hotkey = new Hotkey(Key.Ctrl, Key.F),
            Height = 36,
            FontSize = 18
        };
        var parent = new StackPanel { Children = { viewer } };
        var window = new Window { Content = parent };
        window.AppWindow.Move(new PointInt32(-32000, -32000));
        try
        {
            window.Activate();
            await WaitUntilAsync(() => viewer.IsLoaded);
            CheckKeys(viewer, "Ctrl", "F");

            viewer.Hotkey = new Hotkey(Key.Ctrl, Key.Shift, Key.Left);
            CheckKeys(viewer, "Ctrl", "Shift", "←");
            viewer.IsError = true;
            CheckError(viewer, true);

            // Editing while in the error state must also style newly added keys.
            viewer.Hotkey = new Hotkey(Key.Ctrl, Key.Shift, Key.Alt, Key.F);
            CheckKeys(viewer, "Ctrl", "Shift", "Alt", "F");
            CheckError(viewer, true);
            viewer.IsError = false;
            CheckError(viewer, false);

            viewer.Height = 44;
            viewer.FontSize = 22;
            viewer.UpdateLayout();
            await WaitUntilAsync(() => Buttons(viewer).All(button => button.MinWidth == 44));
            foreach (var button in Buttons(viewer))
            {
                Assert.AreEqual(44d, button.MinWidth);
                Assert.AreEqual(22d, button.FontSize);
                Assert.IsFalse(button.IsHitTestVisible);
            }

            foreach (var theme in new[] { ElementTheme.Dark, ElementTheme.Light })
            {
                viewer.RequestedTheme = theme;
                viewer.IsError = true;
                viewer.UpdateLayout();
                var background = (SolidColorBrush)Buttons(viewer)[0].Background;
                var expected = theme == ElementTheme.Dark
                    ? Windows.UI.Color.FromArgb(255, 68, 39, 38)
                    : Windows.UI.Color.FromArgb(255, 253, 231, 233);
                Assert.AreEqual(expected, background.Color);
            }

            parent.Children.Clear();
            await WaitUntilAsync(() => !viewer.IsLoaded);
            viewer.Hotkey = new Hotkey(Key.F2);
            parent.Children.Add(viewer);
            await WaitUntilAsync(() => viewer.IsLoaded);
            CheckKeys(viewer, "F2");
            CheckError(viewer, true);
            viewer.IsError = false;
            CheckError(viewer, false);

            viewer.Hotkey = Hotkey.Nothing;
            CheckKeys(viewer);
            viewer.Hotkey = new Hotkey(Key.Alt, Key.Enter);
            CheckKeys(viewer, "Alt", "Enter");
            CheckError(viewer, false);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static void CheckKeys(HotkeyViewer viewer, params string[] expected)
    {
        viewer.UpdateLayout();
        var buttons = Buttons(viewer);
        CollectionAssert.AreEqual(expected, buttons.Select(button => (string)button.Content).ToArray());
        for (var i = 1; i < buttons.Length; i++)
        {
            var previous = buttons[i - 1];
            var previousPosition = previous.TransformToVisual(viewer).TransformPoint(default);
            var position = buttons[i].TransformToVisual(viewer).TransformPoint(default);
            Assert.AreEqual(previousPosition.Y, position.Y, 0.1);
            Assert.AreEqual(4d, position.X - previousPosition.X - previous.ActualWidth, 0.1);
        }
    }

    private static void CheckError(HotkeyViewer viewer, bool isError)
    {
        viewer.UpdateLayout();
        foreach (var button in Buttons(viewer))
        {
            Assert.AreEqual(!isError, button.IsChecked);
            if (isError)
                Assert.AreEqual(new Thickness(2), button.BorderThickness);
        }
    }

    private static ToggleButton[] Buttons(DependencyObject root) => Descendants(root).OfType<ToggleButton>().ToArray();

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }
}
