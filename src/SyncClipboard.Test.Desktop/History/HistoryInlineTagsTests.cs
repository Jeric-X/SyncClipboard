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
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.ViewModels.Sub;
using SyncClipboard.Desktop.Controls;
using SyncClipboard.Test.Client.History;

namespace SyncClipboard.Test.Desktop.History;

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
            var visible = InBounds(tags, chip);
            Assert.AreEqual(visible, peer.IsControlElement());
            Assert.AreEqual(!visible, peer.IsOffscreen());
            Assert.AreEqual(visible, peer.IsEnabled());
            if (!visible)
            {
                Assert.ThrowsExactly<ElementNotEnabledException>(((IInvokeProvider)peer).Invoke);
            }
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

    private static void VerifyLongTag(HistoryRecordVM record, Window window, HistoryInlineTagsView tags, Button more)
    {
        const string first = "12312312141515155555555555555555555";
        record.Tags = [first];
        Layout(window);
        Assert.AreEqual(first, ((HistoryPreviewTag)VisibleTags(tags).Single().DataContext!).Name);
        Assert.IsFalse(InBounds(tags, more));

        record.Tags = [first, "33"];
        Layout(window);
        var chip = VisibleTags(tags).Single();
        Assert.AreEqual(first, ((HistoryPreviewTag)chip.DataContext!).Name);
        Assert.AreEqual("+1", more.Content);
        Assert.IsTrue(tags.IsMeasureValid && tags.IsArrangeValid, "Truncated tags must settle after layout.");
        var label = chip.GetVisualDescendants().OfType<TextBlock>().First();
        Assert.IsTrue(label.Bounds.Width > 0 && label.TextLayout.TextLines.Any(line => line.HasCollapsed));
        var remove = chip.GetVisualDescendants().OfType<Button>().Single();
        var removeEnd = remove.TranslatePoint(new Point(remove.Bounds.Width, 0), more);
        Assert.IsTrue(removeEnd.HasValue && removeEnd.Value.X <= 1);
        Click(more);
        var flyout = (Flyout)FlyoutBase.GetAttachedFlyout(more)!;
        var hidden = ((WrapPanel)flyout.Content!).Children.OfType<HistoryRecordTagChip>().Single();
        Assert.AreEqual("33", ((HistoryPreviewTag)hidden.DataContext!).Name);
        flyout.Hide();
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
            try
            {
                window.Show();
                Layout(window);
                var more = tags.Children.OfType<Button>().Single();
                VerifyLongTag(record, window, tags, more);
                record.Tags = names;
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
                window.Width = 200;
                Layout(window);
                Click(more);
                flyout = (Flyout)FlyoutBase.GetAttachedFlyout(more)!;
                Assert.IsTrue(flyout.IsOpen);
                var resizeScroller = ((WrapPanel)flyout.Content!).FindAncestorOfType<ScrollViewer>()!;
                var fullWidth = resizeScroller.Bounds.Width;
                var current = record.Tags;
                record.Tags = ["a", "b", "c"];
                Layout(window);
                Assert.IsTrue(flyout.IsOpen);
                Assert.IsLessThan(fullWidth, resizeScroller.Bounds.Width);
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
                record.Tags = [];
                Layout(window);
                Assert.IsFalse(flyout.IsOpen);
                Assert.IsEmpty(tags.Children);
                Assert.AreEqual(0d, tags.DesiredSize.Width);
                record.Tags = current;
                Layout(window);
                more = tags.Children.OfType<Button>().Single();
                Click(more);
                flyout = (Flyout)FlyoutBase.GetAttachedFlyout(more)!;
                Assert.IsTrue(flyout.IsOpen);
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
    public async Task InlineAndOpenOverflowFollowPinsAndReboundRecords()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryInlineTagsTests));
        await session.Dispatch(async () =>
        {
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            await using var fixture = await HistoryViewModelPagingTests.Fixture.CreateAsync(token);
            await fixture.LoadAsync();
            var record = fixture.Records[0];
            string[] names = ["d", "b", "a", "c", "e", "f", "g", "h"];
            record.Tags = names;
            await fixture.ViewModel.SetPinnedTagsAsync(["missing", "h", "g"]);
            var tags = new HistoryInlineTagsView { DataContext = record, ViewModel = fixture.ViewModel, Height = 40 };
            tags.Bind(HistoryInlineTagsView.TagsProperty, new Binding(nameof(HistoryRecordVM.PreviewTags)));
            tags.Bind(HistoryInlineTagsView.PinnedTagsProperty,
                new Binding(nameof(HistoryViewModel.PinnedTags)) { Source = fixture.ViewModel });
            var window = new Window { Width = 240, Height = 100, Content = tags };
            try
            {
                window.Show();
                Layout(window);
                var more = tags.Children.OfType<Button>().Single();
                Click(more);
                var flyout = (Flyout)FlyoutBase.GetAttachedFlyout(more)!;
                string[] DisplayedNames()
                {
                    Layout(window);
                    Assert.IsTrue(flyout.IsOpen);
                    return VisibleTags(tags).Concat(((WrapPanel)flyout.Content!).Children.OfType<HistoryRecordTagChip>())
                        .Select(chip => chip.GetVisualDescendants().OfType<TextBlock>().First().Text!).ToArray();
                }

                string[] initialOrder = ["h", "g", "a", "b", "c", "d", "e", "f"];
                CollectionAssert.AreEqual(initialOrder, DisplayedNames());
                await fixture.ViewModel.SetPinnedTagsAsync(["g", "h"]);
                string[] reordered = ["g", "h", "a", "b", "c", "d", "e", "f"];
                CollectionAssert.AreEqual(reordered, DisplayedNames());
                await fixture.ViewModel.SetPinnedTagsAsync([]);
                CollectionAssert.AreEqual(names.Order(StringComparer.Ordinal).ToArray(), DisplayedNames());
                CollectionAssert.AreEqual(names, record.Tags);
                CollectionAssert.AreEqual(names, record.ToHistoryRecord().Tags);

                record.Tags = ["z", "a", "h", "b", "c", "d"];
                await fixture.ViewModel.SetPinnedTagsAsync(["h"]);
                string[] updated = ["h", "a", "b", "c", "d", "z"];
                CollectionAssert.AreEqual(updated, DisplayedNames());
                var next = fixture.Records[1];
                next.Tags = ["z", "h"];
                tags.DataContext = next;
                Layout(window);
                Assert.IsFalse(flyout.IsOpen);
                string[] nextOrder = ["h", "z"];
                CollectionAssert.AreEqual(nextOrder, VisibleTags(tags)
                    .Select(chip => chip.GetVisualDescendants().OfType<TextBlock>().First().Text!).ToArray());
                Assert.IsTrue(VisibleTags(tags).All(chip => ((HistoryPreviewTag)chip.DataContext!).Record == next.Key));
            }
            finally
            {
                window.Close();
            }
            return true;
        }, token);
    }

    [TestMethod]
    public async Task MixedLengthInlineTagsKeepTheirLabelsVisible()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryInlineTagsTests));
        await session.Dispatch(() =>
        {
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            string[] names = ["1", "2", "21321", "3", "412213", "99", "A01", "dgsg", "待修复"];
            var tags = new HistoryInlineTagsView
            {
                Tags = names.Select(name => new HistoryPreviewTag(default, name)).ToArray()
            };
            DockPanel.SetDock(tags, Dock.Right);
            var content = new Border { Height = 40 };
            var row = new DockPanel { Children = { tags, content } };
            var window = new Window { Width = 700, Height = 200, Content = new StackPanel { Children = { row } } };
            try
            {
                window.Show();
                Layout(window);
                int[] heights = [40, 140, 40];
                foreach (var height in heights)
                {
                    content.Height = height;
                    Layout(window);
                    foreach (var chip in VisibleTags(tags))
                    {
                        var label = chip.GetVisualDescendants().OfType<TextBlock>().First();
                        var name = ((HistoryPreviewTag)chip.DataContext!).Name;
                        Assert.AreEqual(name, label.Text);
                        Assert.IsGreaterThan(0, label.TextLayout.Width, $"Tag {name} must render visible text.");
                        Assert.IsFalse(label.TextLayout.TextLines.Any(line => line.HasCollapsed),
                            $"Short tag {name} should fit: label width {label.Bounds.Width}, chip width {chip.Bounds.Width}");
                    }
                    Assert.IsGreaterThan(1, VisibleTags(tags).Length);
                    if (height == 140)
                        Assert.AreEqual(names.Length, VisibleTags(tags).Length);
                    Assert.IsTrue(tags.IsMeasureValid && tags.IsArrangeValid);
                }
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
