using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Test.WinUI3.App;
using SyncClipboard.WinUI3.Views;
using Windows.Foundation;
using Windows.Graphics;

namespace SyncClipboard.Test.WinUI3.History;

[TestClass]
[DoNotParallelize]
public class HistoryTagBarAnimationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("PlatformWindows")]
    public async Task PinClickSettlesWithoutReplayingOnTheNextPointerInteraction()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("WinUI needs Windows.");
            return;
        }
        await UITestProcess.RunAsync(RunInWindowAsync, TestContext.CancellationTokenSource.Token);
    }

    internal static async Task RunInWindowAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        var root = new Grid();
        var window = new Window { Content = root };
        window.AppWindow.Move(new PointInt32(40, 40));
        var model = new HistoryTagBarSettingsViewModel(
            new Dictionary<string, int> { ["First"] = 1, ["Move me"] = 1, ["Last"] = 1 }, ["First"]);
        HistoryTagBarSettingsDialog? dialog = null;
        var pointerTrace = new Queue<string>();
        GetCursorPos(out var originalCursor);
        try
        {
            window.Activate();
            await WaitUntilAsync(() => root.IsLoaded, token);
            var scale = root.XamlRoot.RasterizationScale;
            window.AppWindow.ResizeClient(new SizeInt32((int)(850 * scale), (int)(650 * scale)));
            dialog = new HistoryTagBarSettingsDialog(model, () => Task.CompletedTask) { XamlRoot = root.XamlRoot };
            foreach (var (routedEvent, name) in new[]
            {
                (UIElement.PointerPressedEvent, "pressed"), (UIElement.PointerMovedEvent, "moved"),
                (UIElement.PointerReleasedEvent, "released"), (UIElement.PointerCaptureLostEvent, "capture lost")
            })
            {
                dialog.AddHandler(routedEvent, new PointerEventHandler((_, e) =>
                    RecordPointerTrace(dialog, e, name, pointerTrace)), true);
            }
            _ = dialog.ShowAsync();
            await WaitUntilAsync(() => Descendants<ToggleButton>(dialog).Any(), token);
            await Task.Delay(800, token);
            await VerifyClickAsync(window, root, dialog, true, token);
            await VerifyClickAsync(window, root, dialog, false, token);
            await VerifyUnpinnedGhostAsync(window, root, dialog, false, token);
            model.Tags.Single(t => t.Name == "First").IsPinned = true;
            dialog.UpdateLayout();
            await Task.Delay(800, token);
            await VerifyUnpinnedGhostAsync(window, root, dialog, true, token);
        }
        catch (Exception ex)
        {
            throw new AssertFailedException($"Native pointer events:\n{string.Join("\n", pointerTrace)}", ex);
        }
        finally
        {
            SendMouseButton(false);
            model.AcceptChanges();
            dialog?.Hide();
            window.Content = null;
            window.Close();
            SetCursorPos(originalCursor.X, originalCursor.Y);
        }
    }

    private static void RecordPointerTrace(HistoryTagBarSettingsDialog dialog, PointerRoutedEventArgs e,
        string name, Queue<string> trace)
    {
        var point = e.GetCurrentPoint(dialog);
        var viewport = Descendants<ScrollViewer>(dialog).FirstOrDefault(v => v.Name == "ContentScrollViewer");
        var viewportBounds = viewport?.TransformToVisual(dialog)
            .TransformBounds(new Rect(0, 0, viewport.ActualWidth, viewport.ActualHeight));
        var previewCount = Descendants<Canvas>(dialog).FirstOrDefault(c => c.Name == "_DragLayer")?.Children.Count;
        var unpinned = Descendants<ItemsControl>(dialog).FirstOrDefault(c => c.Name == "_UnpinnedTags");
        trace.Enqueue($"{name}: source={e.OriginalSource?.GetType().Name}, position={point.Position}, "
            + $"left={point.Properties.IsLeftButtonPressed}, captures={dialog.PointerCaptures?.Count ?? 0}, "
            + $"preview={previewCount}, viewport={viewportBounds}, unpinned={unpinned?.Items.Count}");
        while (trace.Count > 30)
            trace.Dequeue();
    }

    private static async Task VerifyClickAsync(Window window, Grid root, HistoryTagBarSettingsDialog dialog,
        bool pinned, CancellationToken token)
    {
        var button = Descendants<ToggleButton>(dialog)
            .Single(b => b.DataContext is HistoryTagBarItem { Name: "Move me" });
        MovePointer(window, root, button);
        SendMouseButton(true);
        await Task.Delay(50, token);
        SendMouseButton(false);
        await WaitUntilAsync(() => dialog.Model.Tags.Single(t => t.Name == "Move me").IsPinned == pinned, token);
        await Task.Delay(800, token);
        var pills = Descendants<Border>(dialog).Where(b => b.Name == "TagPill").ToArray();
        var positions = pills.Select(p => p.TransformToVisual(root).TransformPoint(default)).ToArray();
        // A later press previously released the skipped item's stale position and replayed its animation.
        MovePointer(window, root, Descendants<TextBlock>(dialog).Single(t => t.Text == "First"));
        SendMouseButton(true);
        for (var i = 0; i < 100; i++)
        {
            await Task.Delay(20, token);
            dialog.UpdateLayout();
            for (var j = 0; j < pills.Length; j++)
            {
                var position = pills[j].TransformToVisual(root).TransformPoint(default);
                Assert.AreEqual(positions[j].X, position.X, 1d, "Settled pill moved horizontally after a later press.");
                Assert.AreEqual(positions[j].Y, position.Y, 1d, "Settled pill moved vertically after a later press.");
            }
        }
        SendMouseButton(false);
    }

    private static async Task VerifyUnpinnedGhostAsync(Window window, Grid root,
        HistoryTagBarSettingsDialog dialog, bool fromPin, CancellationToken token)
    {
        FrameworkElement source = fromPin
            ? Descendants<ToggleButton>(dialog).Single(b => b.DataContext is HistoryTagBarItem { Name: "First" })
            : Descendants<TextBlock>(dialog).Single(t => t.Text == "First");
        MovePointer(window, root, source);
        SendMouseButton(true);
        await Task.Delay(50, token);
        MovePointer(window, root, Descendants<Border>(dialog).Single(b => b.Name == "_UnpinnedArea"));
        var unpinned = Descendants<ItemsControl>(dialog).Single(c => c.Name == "_UnpinnedTags");
        var item = dialog.Model.Tags.Single(t => t.Name == "First");
        await WaitUntilAsync(() => unpinned.Items.Contains(item), token);
        var ghost = (UIElement)unpinned.ContainerFromItem(item);
        Assert.AreEqual(0.25, ghost.Opacity);
        Assert.IsTrue(item.IsPinned);
        SendMouseButton(false);
        await WaitUntilAsync(() => !item.IsPinned, token);
        dialog.UpdateLayout();
        Assert.AreEqual(1d, ((UIElement)unpinned.ContainerFromItem(item)).Opacity);
    }

    private static void MovePointer(Window window, Grid root, FrameworkElement element)
    {
        var position = element.TransformToVisual(root).TransformPoint(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        var scale = root.XamlRoot.RasterizationScale;
        var point = new NativePoint { X = (int)Math.Round(position.X * scale), Y = (int)Math.Round(position.Y * scale) };
        Assert.IsTrue(ClientToScreen(WinRT.Interop.WindowNative.GetWindowHandle(window), ref point));
        SendMouseInput(new MouseInput
        {
            X = (int)Math.Round((point.X - GetSystemMetrics(76)) * 65535d / (GetSystemMetrics(78) - 1)),
            Y = (int)Math.Round((point.Y - GetSystemMetrics(77)) * 65535d / (GetSystemMetrics(79) - 1)),
            Flags = 0x0001 | 0x8000 | 0x4000 // MOVE | ABSOLUTE | VIRTUALDESK
        });
    }

    private static void SendMouseButton(bool down) =>
        SendMouseInput(new MouseInput { Flags = down ? 0x0002u : 0x0004u });

    private static void SendMouseInput(MouseInput mouse)
    {
        var input = new MouseInputEvent { Mouse = mouse };
        Assert.AreEqual(1u, SendInput(1, [input], Marshal.SizeOf<MouseInputEvent>()));
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match)
            yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i)))
                yield return child;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken token)
    {
        while (!condition())
            await Task.Delay(20, token);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint Data;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputEvent { public uint Type; public MouseInput Mouse; }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, MouseInputEvent[] inputs, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint window, ref NativePoint point);
}
