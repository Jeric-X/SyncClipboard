using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.I18n;
using SyncClipboard.Test.WinUI3.App;
using SyncClipboard.WinUI3.Views;
using Windows.Graphics;

namespace SyncClipboard.Test.WinUI3.History;

[TestClass]
public class HistoryTagBarSettingsTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("PlatformWindows")]
    public async Task PillsPreviewPinsAndFitNarrowWindowsBeforeApply()
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
        window.AppWindow.Move(new PointInt32(-32000, -32000));
        HistoryTagBarSettingsDialog? dialog = null;
        try
        {
            window.Activate();
            await WaitUntilAsync(() => root.IsLoaded, token);
            var model = new HistoryTagBarSettingsViewModel(
                new Dictionary<string, int> { ["Work"] = 2, ["Pinned"] = 1, [new string('W', 60)] = 1, ["Code"] = 5 }, ["Pinned"]);
            var attempts = 0;
            string[] saved = [];
            dialog = new HistoryTagBarSettingsDialog(model, () =>
            {
                if (++attempts == 1)
                    throw new IOException("save failed");
                saved = model.PinnedNames;
                return Task.CompletedTask;
            })
            { XamlRoot = root.XamlRoot };
            var result = dialog.ShowAsync();
            await WaitUntilAsync(() => Descendants<ToggleButton>(dialog).Any(), token);
            Toggle(dialog, "Work");
            await WaitUntilAsync(() => model.PinnedNames.Length == 2, token);
            Assert.AreEqual("Work", model.PinnedNames[^1]);
            model.Move(model.Tags.Single(item => item.Name == "Work"), true, 0);
            dialog.UpdateLayout();
            await Task.Delay(800, token);
            var ordered = Descendants<TextBlock>(dialog)
                .Where(t => t.DataContext is HistoryTagBarItem && t.Text is "Work" or "Pinned")
                .OrderBy(t => t.TransformToVisual(root).TransformPoint(default).X).ToArray();
            Assert.AreEqual("Work", ordered[0].Text);
            Toggle(dialog, "Pinned");
            await WaitUntilAsync(() => model.PinnedNames.Length == 1, token);
            await VerifyNarrowLayoutAsync(window, root, dialog, token);
            Assert.IsEmpty(saved);
            await VerifySaveRetryAsync(dialog, model, token);
            Assert.AreEqual(ContentDialogResult.Primary, await result);
            CollectionAssert.AreEqual(model.PinnedNames, saved);
            // Closing a fresh draft must not call the save delegate.
            dialog = new HistoryTagBarSettingsDialog(model, () => throw new AssertFailedException("Cancel saved the draft"))
            { XamlRoot = root.XamlRoot };
            var cancel = dialog.ShowAsync();
            await WaitUntilAsync(() => Descendants<ToggleButton>(dialog).Any(), token);
            Toggle(dialog, "Code");
            dialog.Hide();
            await WaitUntilAsync(() => Equals(dialog.Title, Strings.HistoryTagBarDiscardTitle), token);
            Invoke(Descendants<Button>(dialog).Single(b => b.Name == "CloseButton"));
            await WaitUntilAsync(() => Equals(dialog.Title, Strings.HistoryTagBarSettings), token);
            Assert.IsTrue(model.HasChanges);
            dialog.Hide();
            await WaitUntilAsync(() => Equals(dialog.Title, Strings.HistoryTagBarDiscardTitle), token);
            Invoke(Descendants<Button>(dialog).Single(b => b.Name == "PrimaryButton"));
            Assert.AreEqual(ContentDialogResult.None, await cancel);
        }
        finally
        {
            dialog?.Model.AcceptChanges();
            dialog?.Hide();
            window.Content = null;
            window.Close();
        }
    }

    private static async Task VerifySaveRetryAsync(HistoryTagBarSettingsDialog dialog,
        HistoryTagBarSettingsViewModel model, CancellationToken token)
    {
        var apply = Descendants<Button>(dialog).Single(b => b.Name == "PrimaryButton");
        Invoke(apply);
        await WaitUntilAsync(() => model.Error == "save failed", token);
        Assert.IsTrue(dialog.IsLoaded);
        Invoke(apply);
    }

    private static async Task VerifyNarrowLayoutAsync(Window window, Grid root,
        HistoryTagBarSettingsDialog dialog, CancellationToken token)
    {
        var scale = root.XamlRoot.RasterizationScale;
        window.AppWindow.ResizeClient(new SizeInt32((int)Math.Round(300 * scale), (int)Math.Round(650 * scale)));
        await WaitUntilAsync(() => Math.Abs(root.ActualWidth - 300) <= 1, token);
        dialog.UpdateLayout();
        await Task.Delay(800, token);
        var buttons = Descendants<ToggleButton>(dialog).ToArray();
        var rows = buttons.Select(b => b.TransformToVisual(root).TransformPoint(default).Y).Distinct().Count();
        Assert.IsGreaterThan(1, rows);
        foreach (var button in buttons)
        {
            var bounds = button.TransformToVisual(root).TransformBounds(new(0, 0, button.ActualWidth, button.ActualHeight));
            Assert.IsGreaterThanOrEqualTo(-1d, bounds.Left);
            Assert.IsLessThanOrEqualTo(root.ActualWidth + 1, bounds.Right);
        }
    }

    private static void Toggle(HistoryTagBarSettingsDialog dialog, string name)
    {
        var button = Descendants<ToggleButton>(dialog).Single(b => b.DataContext is HistoryTagBarItem item && item.Name == name);
        ((IToggleProvider)new ToggleButtonAutomationPeer(button).GetPattern(PatternInterface.Toggle)).Toggle();
    }

    private static void Invoke(Button button) =>
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();

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
