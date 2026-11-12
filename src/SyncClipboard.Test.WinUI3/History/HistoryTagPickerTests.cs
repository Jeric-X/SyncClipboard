using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Test.WinUI3.App;
using SyncClipboard.WinUI3.Views;
using Windows.Foundation;
using Windows.Graphics;

namespace SyncClipboard.Test.WinUI3.History;

[TestClass]
public class HistoryTagPickerTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("PlatformWindows")]
    public async Task PickerSearchAndContinuousEditingPreserveInputAndScroll()
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
        var saved = new List<(string Name, bool Add)>();
        var pending = new TaskCompletionSource();
        var picker = new HistoryTagPickerViewModel(["code", "work", .. Enumerable.Range(1, 20).Select(i => $"tag{i:D2}")], ["work"], (name, add) =>
        {
            saved.Add((name, add));
            return saved.Count == 1 ? pending.Task : Task.CompletedTask;
        });
        var view = new HistoryTagPickerView(picker);
        var bar = new RecordControlBar();
        var window = new Window { Content = bar };
        window.AppWindow.Move(new PointInt32(-32000, -32000));
        Flyout? flyout = null;
        try
        {
            window.Activate();
            await WaitUntilAsync(() => bar.IsLoaded, timeout.Token);
            flyout = view.ShowAt((Button)bar.FindName("_AddTagButton"));
            await WaitUntilAsync(() => view.IsLoaded && Search(view).FocusState != FocusState.Unfocused, timeout.Token);
            var presenter = VisualTreeHelper.GetOpenPopupsForXamlRoot(view.XamlRoot)
                .SelectMany(p => Descendants<FlyoutPresenter>(p.Child)).Single();
            Assert.AreEqual((CornerRadius)Application.Current.Resources["OverlayCornerRadius"], presenter.CornerRadius);
            await VerifySearchPositionAsync(view, timeout.Token);
            await VerifySearchAndRetryAsync(view, pending, saved, timeout.Token);
            await VerifyContinuousEditingAsync(view, saved, timeout.Token);
            Assert.IsTrue(flyout.IsOpen);
        }
        finally
        {
            flyout?.Hide();
            window.Content = null;
            window.Close();
        }
    }

    private static async Task VerifySearchPositionAsync(HistoryTagPickerView view, CancellationToken token)
    {
        var search = Search(view);
        view.UpdateLayout();
        var position = search.TransformToVisual(null).TransformPoint(new Point());
        var height = view.ActualHeight;
        search.Text = "new tag";
        await WaitUntilAsync(() => Suggestions(view).Count() == 1, token);
        view.UpdateLayout();
        Assert.AreEqual(height, view.ActualHeight, 1d);
        var filteredPosition = search.TransformToVisual(null).TransformPoint(new Point());
        Assert.AreEqual(position.X, filteredPosition.X, 1d);
        Assert.AreEqual(position.Y, filteredPosition.Y, 1d);

        search.Text = "";
        await WaitUntilAsync(() => Suggestions(view).Count() == 22, token);
        view.UpdateLayout();
        Assert.AreEqual(height, view.ActualHeight, 1d);
        var restoredPosition = search.TransformToVisual(null).TransformPoint(new Point());
        Assert.AreEqual(position.X, restoredPosition.X, 1d);
        Assert.AreEqual(position.Y, restoredPosition.Y, 1d);
    }

    private static async Task VerifySearchAndRetryAsync(HistoryTagPickerView view, TaskCompletionSource pending,
        List<(string Name, bool Add)> saved, CancellationToken token)
    {
        var search = Search(view);
        search.Text = "cod";
        await WaitUntilAsync(() => Suggestions(view).Count() == 2, token);
        var code = Suggestions(view).Single(b => Equals(b.Tag, "code"));
        Assert.AreEqual("code", code.Tag);
        await WaitUntilAsync(() => code.ActualWidth > 100, token);
        Invoke(code);
        await WaitUntilAsync(() => saved.Count == 1, token);
        Assert.AreSame(search, FocusManager.GetFocusedElement(view.XamlRoot));
        search.Text = "next";
        pending.SetException(new InvalidOperationException("save failed"));
        await WaitUntilAsync(() => view.ViewModel.Error == "save failed", token);
        Assert.AreEqual("next", search.Text);
        Assert.IsFalse(view.ViewModel.Suggestions.Any(s => s.IsAssigned));
        Assert.IsTrue(Descendants<TextBlock>(view).Any(t => t.Text == "save failed" && t.Visibility == Visibility.Visible));
        CollectionAssert.AreEqual(new[] { ("code", true) }, saved);
    }

    private static async Task VerifyContinuousEditingAsync(HistoryTagPickerView view,
        List<(string Name, bool Add)> saved, CancellationToken token)
    {
        var search = Search(view);
        await WaitUntilAsync(() => Suggestions(view).Any(b => Equals(b.Tag, "next")), token);
        var candidate = Suggestions(view).Single(b => Equals(b.Tag, "next"));
        Assert.IsTrue(view.ViewModel.Suggestions.Single().IsNew);
        Invoke(candidate);
        await WaitUntilAsync(() => search.Text.Length == 0 && Suggestions(view).Count() == 23, token);
        Assert.AreEqual(("next", true), saved[^1]);
        var code = Suggestions(view).Single(b => Equals(b.Tag, "code"));
        Assert.IsFalse(view.ViewModel.Suggestions.Single(s => s.Name == "code").IsAssigned);
        Invoke(code);
        await WaitUntilAsync(() => view.ViewModel.Suggestions.Single(s => s.Name == "code").IsAssigned, token);
        Assert.AreEqual(("code", true), saved[^1]);
        var work = Suggestions(view).Single(b => Equals(b.Tag, "work"));
        Invoke(work);
        await WaitUntilAsync(() => !view.ViewModel.Suggestions.Single(s => s.Name == "work").IsAssigned, token);
        Assert.AreEqual(("work", false), saved[^1]);
        Assert.AreSame(search, FocusManager.GetFocusedElement(view.XamlRoot));
        await VerifyScrollAndCheckmarkAsync(view, token);
    }

    private static async Task VerifyScrollAndCheckmarkAsync(HistoryTagPickerView view, CancellationToken token)
    {
        var scroll = Descendants<ScrollViewer>(view).Single(s => s.Content is ItemsControl);
        Assert.IsTrue(scroll.ViewportHeight <= 200 && scroll.ScrollableHeight > 0);
        scroll.ChangeView(null, scroll.ScrollableHeight, null, disableAnimation: true);
        await WaitUntilAsync(() => scroll.VerticalOffset > 0, token);
        var offset = scroll.VerticalOffset;
        var tag = Suggestions(view).Single(b => Equals(b.Tag, "tag20"));
        var checkmark = Descendants<TextBlock>(tag).Single(t => t.Text == "✓");
        Assert.AreEqual(Visibility.Collapsed, checkmark.Visibility);
        Invoke(tag);
        await WaitUntilAsync(() => checkmark.Visibility == Visibility.Visible, token);
        Assert.AreEqual(offset, scroll.VerticalOffset, 1d);
        Invoke(tag);
        await WaitUntilAsync(() => checkmark.Visibility == Visibility.Collapsed, token);
        Assert.AreEqual(offset, scroll.VerticalOffset, 1d);
    }

    private static TextBox Search(HistoryTagPickerView view) => (TextBox)view.FindName("_TagSearch");

    private static IEnumerable<Button> Suggestions(DependencyObject root) => Descendants<Button>(root).Where(b => b.Tag is string);

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

    private static void Invoke(Button button) =>
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken token)
    {
        while (!condition())
            await Task.Delay(20, token);
    }
}
