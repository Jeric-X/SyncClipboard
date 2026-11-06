using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Test.WinUI3.App;
using SyncClipboard.WinUI3.Views;
using Windows.Graphics;

namespace SyncClipboard.Test.WinUI3.History;

[TestClass]
public class HistoryTagEditorDialogTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("PlatformWindows")]
    public async Task EditorKeepsItsWidthAcrossSearchesAndFitsWhenWindowShrinks()
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
        var root = new Grid();
        var window = new Window { Content = root };
        window.AppWindow.Move(new PointInt32(-32000, -32000));
        HistoryTagEditorDialog? dialog = null;
        try
        {
            window.Activate();
            await WaitUntilAsync(() => root.IsLoaded, timeout.Token);
            await ResizeAsync(window, root, 850, timeout.Token);
            var vm = new HistoryTagEditorViewModel(["short", new string('W', 100)], [[]]);
            dialog = new HistoryTagEditorDialog(vm, () => Task.CompletedTask) { XamlRoot = root.XamlRoot };
            var result = dialog.ShowAsync();
            await WaitUntilAsync(() => Descendants<Border>(dialog).Any(b => b.Name == "BackgroundElement" && b.ActualWidth > 0), timeout.Token);
            var background = Descendants<Border>(dialog).Single(b => b.Name == "BackgroundElement");
            await Task.Delay(300, timeout.Token);
            var wideWidth = background.ActualWidth;
            Assert.IsLessThanOrEqualTo(400, wideWidth);
            vm.Search = "short";
            dialog.UpdateLayout();
            Assert.AreEqual(wideWidth, background.ActualWidth, 1d);
            await VerifyNarrowWindowAsync(window, root, dialog, background, wideWidth, timeout.Token);
            await ResizeAsync(window, root, 850, timeout.Token);
            await WaitUntilAsync(() => Math.Abs(background.ActualWidth - wideWidth) <= 1, timeout.Token);
            dialog.Hide();
            await result;
        }
        finally
        {
            dialog?.Hide();
            window.Content = null;
            window.Close();
        }
    }

    private static async Task VerifyNarrowWindowAsync(Window window, Grid root, HistoryTagEditorDialog dialog,
        Border background, double wideWidth, CancellationToken token)
    {
        await ResizeAsync(window, root, 300, token);
        await WaitUntilAsync(() => background.ActualWidth < wideWidth, token);
        var search = Descendants<TextBox>(dialog).Single();
        var apply = Descendants<Button>(dialog).Single(b => b.Name == "PrimaryButton");
        foreach (var control in new FrameworkElement[] { background, search, apply })
        {
            var bounds = control.TransformToVisual(root).TransformBounds(new(0, 0, control.ActualWidth, control.ActualHeight));
            Assert.IsGreaterThanOrEqualTo(-1d, bounds.Left);
            Assert.IsLessThanOrEqualTo(root.ActualWidth + 1, bounds.Right);
        }
    }

    private static async Task ResizeAsync(Window window, Grid root, double width, CancellationToken token)
    {
        var scale = root.XamlRoot.RasterizationScale;
        window.AppWindow.ResizeClient(new SizeInt32((int)Math.Round(width * scale), (int)Math.Round(650 * scale)));
        await WaitUntilAsync(() => Math.Abs(root.ActualWidth - width) <= 1, token);
        root.UpdateLayout();
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
}
