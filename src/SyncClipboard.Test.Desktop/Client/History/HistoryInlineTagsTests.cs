using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.Styling;
using Moq;
using SyncClipboard.Core.Interfaces;
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
            bool InBounds(Control child) => child.Bounds.Width > 0 && child.Bounds.Right <= tags.Bounds.Width + 0.1;
            HistoryRecordTagChip[] VisibleTags() => tags.Children.OfType<HistoryRecordTagChip>().Where(InBounds).ToArray();
            void VerifyAccessibleButtons()
            {
                foreach (var chip in tags.Children.OfType<HistoryRecordTagChip>())
                {
                    var remove = chip.GetVisualDescendants().OfType<Button>().Single();
                    var peer = ControlAutomationPeer.CreatePeerForElement(remove)!;
                    Assert.AreEqual(InBounds(chip), peer.IsControlElement());
                    Assert.AreEqual(!InBounds(chip), peer.IsOffscreen());
                    Assert.AreEqual(InBounds(chip), peer.IsEnabled());
                }
                Assert.AreEqual(InBounds(more), ControlAutomationPeer.CreatePeerForElement(more)!.IsControlElement());
            }

            void Layout()
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            }

            async Task RemoveAndRefresh(Button button)
            {
                var saved = new TaskCompletionSource<HistoryRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnSaved(HistoryRecord updated) => saved.TrySetResult(updated);
                fixture.Manager.HistoryUpdated += OnSaved;
                try
                {
                    Click(button);
                    var update = await saved.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                    fixture.Publish(new HistoryRecordVM(update));
                    Layout();
                }
                finally
                {
                    fixture.Manager.HistoryUpdated -= OnSaved;
                }
            }

            try
            {
                window.Show();
                Layout();
                Assert.AreEqual(40, row.Bounds.Height);
                VerifyAccessibleButtons();
                var hiddenChip = tags.Children.OfType<HistoryRecordTagChip>().First(chip => !InBounds(chip));
                var hiddenButton = hiddenChip.GetVisualDescendants().OfType<Button>().Single();
                var invoke = (IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(hiddenButton)!;
                Assert.ThrowsExactly<ElementNotEnabledException>(invoke.Invoke);
                CollectionAssert.AreEqual(names, (await fixture.ReadStoredRecordAsync(record)).Tags);
                var shortCount = VisibleTags().Length;
                Assert.IsGreaterThan(0, shortCount);
                Assert.IsTrue(InBounds(more));
                Assert.AreEqual($"+{names.Length - shortCount}", more.Content);
                content.Height = 100;
                Layout();
                Assert.AreEqual(100, row.Bounds.Height);
                VerifyAccessibleButtons();
                Assert.IsGreaterThan(shortCount, VisibleTags().Length);
                Assert.AreEqual($"+{names.Length - VisibleTags().Length}", more.Content);
                Assert.IsTrue(VisibleTags().All(chip => chip.Bounds.Bottom <= row.Bounds.Height));
                window.Width = 240;
                Layout();
                Assert.IsLessThanOrEqualTo(row.Bounds.Width / 2 + 1, tags.Bounds.Width);
                Assert.AreEqual(100, row.Bounds.Height);

                Click(more);
                var flyout = (Flyout)FlyoutBase.GetAttachedFlyout(more)!;
                Assert.IsTrue(flyout.IsOpen);
                Assert.AreSame(more, flyout.Target);
                Assert.IsTrue(fixture.ViewModel.HasModalOperation);
                var popup = (WrapPanel)flyout.Content!;
                var scroller = popup.FindAncestorOfType<ScrollViewer>()!;
                Assert.IsGreaterThan(scroller.Viewport.Height, scroller.Extent.Height);
                var fullWidth = scroller.Bounds.Width;
                var oldY = popup.TranslatePoint(default, window)!.Value.Y;
                row.Margin = new Thickness(0, 18, 0, 0);
                Layout();
                Assert.AreEqual(oldY + 18, popup.TranslatePoint(default, window)!.Value.Y, 1);
                string?[] Labels() => popup.GetVisualDescendants().OfType<TextBlock>()
                    .Where(label => label.FindAncestorOfType<Button>() is null).Select(label => label.Text).ToArray();
                CollectionAssert.AreEqual(names[VisibleTags().Length..], Labels());
                content.Height = 40;
                Layout();
                CollectionAssert.AreEqual(names[VisibleTags().Length..], Labels());

                var removed = Labels().First()!;
                await RemoveAndRefresh(popup.GetVisualDescendants().OfType<Button>()
                    .Single(b => b.DataContext is HistoryPreviewTag tag && tag.Name == removed));
                var remaining = names.Where(n => n != removed).ToArray();
                CollectionAssert.AreEqual(remaining, (await fixture.ReadStoredRecordAsync(record)).Tags);
                CollectionAssert.AreEqual(names, (await fixture.ReadStoredRecordAsync(other)).Tags);
                CollectionAssert.AreEqual(remaining[VisibleTags().Length..], Labels());
                Assert.AreEqual($"+{remaining.Length - VisibleTags().Length}", more.Content);
                Assert.IsTrue(flyout.IsOpen);

                fixture.ViewModel.SelectedItem = other;
                Layout();
                Assert.IsFalse(flyout.IsOpen);
                Assert.IsFalse(fixture.ViewModel.HasModalOperation);
                window.Width = 440;
                Layout();
                var firstChip = VisibleTags().First();
                var visibleTag = ((HistoryPreviewTag)firstChip.DataContext!).Name;
                await RemoveAndRefresh(firstChip.GetVisualDescendants().OfType<Button>().Single());
                Assert.DoesNotContain(visibleTag, (await fixture.ReadStoredRecordAsync(record)).Tags);
                CollectionAssert.AreEqual(names, (await fixture.ReadStoredRecordAsync(other)).Tags);
                Click(more);
                flyout = (Flyout)FlyoutBase.GetAttachedFlyout(more)!;
                Assert.IsTrue(flyout.IsOpen);
                var current = record.Tags;
                window.Width = 200;
                record.Tags = ["tag00000", "tag11111", "md"];
                Layout();
                var smallPopup = (WrapPanel)flyout.Content!;
                Assert.IsLessThan(fullWidth, smallPopup.FindAncestorOfType<ScrollViewer>()!.Bounds.Width);
                record.Tags = ["md"];
                Layout();
                Assert.IsFalse(flyout.IsOpen);
                Assert.IsFalse(fixture.ViewModel.HasModalOperation);
                Assert.IsFalse(InBounds(more));
                VerifyAccessibleButtons();
                window.Width = 440;
                record.Tags = current;
                Layout();
                Click(more);
                flyout = (Flyout)FlyoutBase.GetAttachedFlyout(more)!;
                row.Children.Remove(tags);
                Layout();
                Assert.IsFalse(flyout.IsOpen);
                Assert.IsFalse(fixture.ViewModel.HasModalOperation);
            }
            finally
            {
                window.Close();
            }
            return true;
        }, token);
    }

    [TestMethod]
    public async Task OverflowClosesOnEscapeOrWhenTheTriggerScrollsOutOfView()
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
            var search = new TextBox();
            var list = new ScrollViewer { Height = 100, Content = new StackPanel { Children = { tags, new Border { Height = 400 } } } };
            var owner = new Window { Width = 220, Height = 300, Content = new StackPanel { Children = { search, list } } };
            try
            {
                owner.Show();
                search.Focus();
                Dispatcher.UIThread.RunJobs();
                owner.UpdateLayout();
                var more = tags.Children.OfType<Button>().Single();
                Click(more);
                var flyout = (Flyout)FlyoutBase.GetAttachedFlyout(more)!;
                Assert.IsTrue(flyout.IsOpen);
                var scroll = ((Control)flyout.Content!).FindAncestorOfType<ScrollViewer>()!;
                owner.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.PageDown, null);
                owner.KeyRelease(Key.PageDown, RawInputModifiers.None, PhysicalKey.PageDown, null);
                Dispatcher.UIThread.RunJobs();
                Assert.IsGreaterThan(0d, scroll.Offset.Y);
                owner.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                owner.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Dispatcher.UIThread.RunJobs();
                Assert.IsFalse(flyout.IsOpen);
                Assert.AreSame(search, owner.FocusManager!.GetFocusedElement());

                Assert.IsTrue(tags.TryShowHiddenTags());
                flyout = (Flyout)FlyoutBase.GetAttachedFlyout(more)!;
                list.Offset = new Vector(0, 150);
                Dispatcher.UIThread.RunJobs();
                owner.UpdateLayout();
                Assert.IsFalse(flyout.IsOpen);
                Assert.IsFalse(fixture.ViewModel.HasModalOperation);
                Assert.IsFalse(tags.TryShowHiddenTags());
            }
            finally
            {
                owner.Close();
            }
            return true;
        }, token);
    }

    [TestMethod]
    public async Task DismissingOverflowAfterDeactivationAppliesCloseWhenLostFocus()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryInlineTagsTests));
        await session.Dispatch(async () =>
        {
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            await using var fixture = await HistoryViewModelPagingTests.Fixture.CreateAsync(token);
            await fixture.LoadAsync();
            var record = fixture.Records[0];
            record.Tags = Enumerable.Range(0, 12).Select(i => $"tag{i}").ToArray();
            var tags = new HistoryInlineTagsView { DataContext = record, Tags = record.PreviewTags, ViewModel = fixture.ViewModel };
            var owner = new Window { Width = 200, Height = 40, Content = tags, ShowActivated = false };
            var historyWindow = new Mock<IWindow>();
            historyWindow.Setup(w => w.Hide()).Callback(owner.Hide);
            HistoryViewModelPagingTests.Fixture.SetField(fixture.ViewModel, "window", historyWindow.Object);
            fixture.ViewModel.CloseWhenLostFocus = true;
            try
            {
                owner.Show();
                Dispatcher.UIThread.RunJobs();
                owner.UpdateLayout();
                var more = tags.Children.OfType<Button>().Single();
                Click(more);
                var flyout = (Flyout)FlyoutBase.GetAttachedFlyout(more)!;
                Assert.IsTrue(flyout.IsOpen);
                fixture.ViewModel.OnLostFocus();
                Assert.IsFalse(owner.IsActive);
                Assert.IsTrue(owner.IsVisible);
                flyout.Hide();
                Dispatcher.UIThread.RunJobs();
                Assert.IsFalse(owner.IsVisible);
                Assert.IsFalse(fixture.ViewModel.HasModalOperation);
            }
            finally
            {
                owner.Close();
            }
            return true;
        }, token);
    }
}
