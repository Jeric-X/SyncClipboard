using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.Styling;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.ViewModels.Sub;
using SyncClipboard.Desktop.Controls;
using SyncClipboard.Test.Client.History;

namespace SyncClipboard.Test.Desktop.Client.History;

[TestClass]
[DoNotParallelize]
public class HistoryInlineTagsTests
{
    public TestContext TestContext { get; set; } = null!;

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());

    private static void Click(Button button)
    {
        var top = TopLevel.GetTopLevel(button)!;
        top.UpdateLayout();
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), top)!.Value;
        top.MouseMove(point);
        top.MouseDown(point, MouseButton.Left);
        top.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static bool InBounds(HistoryInlineTagsView tags, Control child) => child.Bounds.Width > 0 && child.Bounds.Right <= tags.Bounds.Width + 0.1;

    private static HistoryRecordTagChip[] VisibleTags(HistoryInlineTagsView tags) => tags.Children.OfType<HistoryRecordTagChip>().Where(chip => InBounds(tags, chip)).ToArray();

    private static void VerifyAccessibleButtons(HistoryInlineTagsView tags, Button more)
    {
        foreach (var chip in tags.Children.OfType<HistoryRecordTagChip>())
        {
            var remove = chip.GetVisualDescendants().OfType<Button>().Single();
            var peer = ControlAutomationPeer.CreatePeerForElement(remove)!;
            Assert.AreEqual(InBounds(tags, chip), peer.IsControlElement());
            Assert.AreEqual(!InBounds(tags, chip), peer.IsOffscreen());
            Assert.AreEqual(InBounds(tags, chip), peer.IsEnabled());
        }
        Assert.AreEqual(InBounds(tags, more), ControlAutomationPeer.CreatePeerForElement(more)!.IsControlElement());
    }

    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static async Task RemoveAndRefresh(HistoryViewModelPagingTests.Fixture fixture, Button button, Window window, CancellationToken token)
    {
        var saved = new TaskCompletionSource<HistoryRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnSaved(HistoryRecord updated) => saved.TrySetResult(updated);
        fixture.Manager.HistoryUpdated += OnSaved;
        try
        {
            Click(button);
            var update = await saved.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            fixture.Publish(new HistoryRecordVM(update));
            Layout(window);
        }
        finally
        {
            fixture.Manager.HistoryUpdated -= OnSaved;
        }
    }

    private static void VerifyRowLayout(Window window, DockPanel row, Border content, HistoryInlineTagsView tags, Button more, int tagCount)
    {
        Assert.AreEqual(40, row.Bounds.Height);
        VerifyAccessibleButtons(tags, more);
        Assert.IsLessThan(tagCount, tags.Children.OfType<HistoryRecordTagChip>().Count());
        var hiddenChip = tags.Children.OfType<HistoryRecordTagChip>().First(chip => !InBounds(tags, chip));
        var hiddenButton = hiddenChip.GetVisualDescendants().OfType<Button>().Single();
        var invoke = (IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(hiddenButton)!;
        Assert.ThrowsExactly<ElementNotEnabledException>(invoke.Invoke);
        var shortCount = VisibleTags(tags).Length;
        Assert.IsGreaterThan(0, shortCount);
        Assert.IsTrue(InBounds(tags, more));
        Assert.AreEqual($"+{tagCount - shortCount}", more.Content);
        content.Height = 100;
        Layout(window);
        Assert.AreEqual(100, row.Bounds.Height);
        VerifyAccessibleButtons(tags, more);
        Assert.IsGreaterThan(shortCount, VisibleTags(tags).Length);
        Assert.AreEqual($"+{tagCount - VisibleTags(tags).Length}", more.Content);
        Assert.IsTrue(VisibleTags(tags).All(chip => chip.Bounds.Bottom <= row.Bounds.Height));
        window.Width = 240;
        Layout(window);
        var halfRowWidth = row.Bounds.Width / 2;
        Assert.IsLessThanOrEqualTo(halfRowWidth + 1, tags.Bounds.Width);
        Assert.AreEqual(100, row.Bounds.Height);
    }

    [TestMethod]
    public async Task TagsFitTheContentHeightAndOverflowUpdatesTheCorrectRecord()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryInlineTagsTests));
        await session.Dispatch(async () =>
        {
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            await using var fixture = await HistoryViewModelPagingTests.Fixture.CreateAsync(token);
            await fixture.LoadAsync();
            var record = fixture.Records[0];
            var other = fixture.Records[1];
            var names = Enumerable.Range(0, 16).Select(i => $"tag{i:D2}").ToArray();
            await fixture.Manager.EditTagsAsync([record.Key, other.Key], names, [], token);
            record.Tags = names;
            fixture.ViewModel.EnterMultiSelect(record);
            fixture.ViewModel.EnterMultiSelect(other);
            fixture.ViewModel.SelectedItem = record;
            var tags = new HistoryInlineTagsView { DataContext = record, ViewModel = fixture.ViewModel };
            tags.Bind(HistoryInlineTagsView.TagsProperty, new Binding(nameof(HistoryRecordVM.PreviewTags)));
            DockPanel.SetDock(tags, Dock.Right);
            var content = new Border { Height = 40 };
            var row = new DockPanel { Children = { tags, content } };
            var window = new Window { Width = 440, Height = 350, Content = new StackPanel { Children = { row } } };
            var more = tags.Children.OfType<Button>().Single();

            try
            {
                window.Show();
                Layout(window);
                VerifyRowLayout(window, row, content, tags, more, names.Length);
                CollectionAssert.AreEqual(names, (await fixture.ReadStoredRecordAsync(record)).Tags);

                Click(more);
                var flyout = (Flyout)FlyoutBase.GetAttachedFlyout(more)!;
                Assert.IsTrue(flyout.IsOpen);
                Assert.AreSame(more, flyout.Target);
                var popup = (WrapPanel)flyout.Content!;
                var scroller = popup.FindAncestorOfType<ScrollViewer>()!;
                Assert.IsGreaterThan(scroller.Viewport.Height, scroller.Extent.Height);
                var fullWidth = scroller.Bounds.Width;
                var oldY = popup.TranslatePoint(default, window)!.Value.Y;
                row.Margin = new Thickness(0, 18, 0, 0);
                Layout(window);
                Assert.AreEqual(oldY + 18, popup.TranslatePoint(default, window)!.Value.Y, 1);
                string?[] Labels() => popup.GetVisualDescendants().OfType<TextBlock>()
                    .Where(label => label.FindAncestorOfType<Button>() is null).Select(label => label.Text).ToArray();
                CollectionAssert.AreEqual(names[VisibleTags(tags).Length..], Labels());
                content.Height = 40;
                Layout(window);
                CollectionAssert.AreEqual(names[VisibleTags(tags).Length..], Labels());

                var removed = Labels().First()!;
                await RemoveAndRefresh(fixture, popup.GetVisualDescendants().OfType<Button>()
                    .Single(b => b.DataContext is HistoryPreviewTag tag && tag.Name == removed), window, token);
                var remaining = names.Where(n => n != removed).ToArray();
                CollectionAssert.AreEqual(remaining, (await fixture.ReadStoredRecordAsync(record)).Tags);
                CollectionAssert.AreEqual(names, (await fixture.ReadStoredRecordAsync(other)).Tags);
                CollectionAssert.AreEqual(remaining[VisibleTags(tags).Length..], Labels());
                Assert.AreEqual($"+{remaining.Length - VisibleTags(tags).Length}", more.Content);
                Assert.IsTrue(flyout.IsOpen);

                fixture.ViewModel.SelectedItem = other;
                Layout(window);
                Assert.IsFalse(flyout.IsOpen);
                window.Width = 440;
                Layout(window);
                var firstChip = VisibleTags(tags).First();
                var visibleTag = ((HistoryPreviewTag)firstChip.DataContext!).Name;
                await RemoveAndRefresh(fixture, firstChip.GetVisualDescendants().OfType<Button>().Single(), window, token);
                Assert.DoesNotContain(visibleTag, (await fixture.ReadStoredRecordAsync(record)).Tags);
                CollectionAssert.AreEqual(names, (await fixture.ReadStoredRecordAsync(other)).Tags);
                Click(more);
                flyout = (Flyout)FlyoutBase.GetAttachedFlyout(more)!;
                Assert.IsTrue(flyout.IsOpen);
                var current = record.Tags;
                window.Width = 200;
                record.Tags = ["tag00000", "tag11111", "md"];
                Layout(window);
                var smallPopup = (WrapPanel)flyout.Content!;
                Assert.IsLessThan(fullWidth, smallPopup.FindAncestorOfType<ScrollViewer>()!.Bounds.Width);
                record.Tags = ["md"];
                Layout(window);
                Assert.IsFalse(flyout.IsOpen);
                Assert.IsFalse(InBounds(tags, more));
                VerifyAccessibleButtons(tags, more);
                window.Width = 440;
                record.Tags = current;
                Layout(window);
                Click(more);
                flyout = (Flyout)FlyoutBase.GetAttachedFlyout(more)!;
                row.Children.Remove(tags);
                Layout(window);
                Assert.IsFalse(flyout.IsOpen);
            }
            finally
            {
                window.Close();
            }
            return true;
        }, token);
    }

    [TestMethod]
    public async Task OverflowClosesWhenTheTriggerScrollsOutOfView()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryInlineTagsTests));
        await session.Dispatch(async () =>
        {
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            await using var fixture = await HistoryViewModelPagingTests.Fixture.CreateAsync(token);
            await fixture.LoadAsync();
            var record = fixture.Records[0];
            record.Tags = Enumerable.Range(0, 28).Select(i => $"tag{i}").ToArray();
            var tags = new HistoryInlineTagsView { Height = 40, Margin = new Thickness(0, 0, 32, 0), DataContext = record, Tags = record.PreviewTags, ViewModel = fixture.ViewModel };
            var list = new ScrollViewer { Height = 100, Content = new StackPanel { Children = { tags, new Border { Height = 400 } } } };
            var owner = new Window { Width = 220, Height = 300, Content = list };
            try
            {
                owner.Show();
                Dispatcher.UIThread.RunJobs();
                owner.UpdateLayout();
                var more = tags.Children.OfType<Button>().Single();
                Click(more);
                var flyout = (Flyout)FlyoutBase.GetAttachedFlyout(more)!;
                Assert.IsTrue(flyout.IsOpen);
                list.Offset = new Vector(0, 150);
                Dispatcher.UIThread.RunJobs();
                owner.UpdateLayout();
                Assert.IsFalse(flyout.IsOpen);
            }
            finally
            {
                owner.Close();
            }
            return true;
        }, token);
    }
}
