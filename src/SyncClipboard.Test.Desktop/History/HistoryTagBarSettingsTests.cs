using System.Globalization;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.Styling;
using FluentAvalonia.UI.Controls;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.I18n;
using SyncClipboard.Desktop.Views;

namespace SyncClipboard.Test.Desktop.History;

[TestClass]
[DoNotParallelize]
public class HistoryTagBarSettingsTests
{
    public TestContext TestContext { get; set; } = null!;

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());

    [TestMethod]
    [DataRow("en-US")]
    [DataRow("zh-CN")]
    public async Task PinsPreviewInWrappingPillsAndCancelLeavesSavedOrderAlone(string cultureName)
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryTagBarSettingsTests));
        await session.Dispatch(async () =>
        {
            var previousCulture = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            var model = CreateModel();
            var saveCount = 0;
            var dialog = new HistoryTagBarSettingsDialog(model, () =>
            {
                saveCount++;
                return Task.CompletedTask;
            });
            var owner = new Window { Width = 850, Height = 650, Content = new Button() };
            try
            {
                owner.Show();
                var result = dialog.ShowAsync(owner);
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(800, token);
                Dispatcher.UIThread.RunJobs();
                VerifyDialogBounds(dialog, owner);
                var pills = dialog.GetVisualDescendants().OfType<ToggleButton>().ToArray();
                foreach (var pill in pills)
                    Assert.IsLessThanOrEqualTo(28, ((Control)pill.Parent!.Parent!).Bounds.Height);
                Toggle(dialog, "Work");
                Assert.AreEqual("Work", model.PinnedNames[^1]);
                Assert.IsTrue(model.Tags.Single(item => item.Name == "Work").IsPinned);
                model.Move(model.Tags.Single(item => item.Name == "Work"), true, 0);
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(800, token);
                Dispatcher.UIThread.RunJobs();
                var ordered = dialog.GetVisualDescendants().OfType<TextBlock>()
                    .Where(t => t.DataContext is HistoryTagBarItem && t.Text is "Work" or "Pinned")
                    .OrderBy(t => t.TranslatePoint(default, owner)!.Value.X).ToArray();
                Assert.AreEqual("Work", ordered[0].Text);
                Toggle(dialog, "Pinned");
                Assert.IsFalse(model.Tags.Single(item => item.Name == "Pinned").IsPinned);
                owner.Width = 300;
                owner.Height = 400;
                Dispatcher.UIThread.RunJobs();
                owner.UpdateLayout();
                await Task.Delay(800, token);
                Dispatcher.UIThread.RunJobs();
                var panel = (WrapPanel)dialog.FindControl<ItemsControl>("_UnpinnedTags")!.ItemsPanelRoot!;
                Assert.IsGreaterThan(1, panel.Children.Select(c => c.Bounds.Y).Distinct().Count());
                VerifyDialogBounds(dialog, owner);
                foreach (var toggle in dialog.GetVisualDescendants().OfType<ToggleButton>())
                {
                    var point = toggle.TranslatePoint(default, owner)!.Value;
                    Assert.IsGreaterThanOrEqualTo(0, point.X);
                    Assert.IsLessThanOrEqualTo(owner.Bounds.Width, point.X + toggle.Bounds.Width);
                }
                owner.Width = 1200;
                owner.Height = 650;
                Dispatcher.UIThread.RunJobs();
                owner.UpdateLayout();
                VerifyDialogBounds(dialog, owner);
                Invoke(dialog, "CloseButton");
                Assert.AreEqual(Strings.HistoryTagBarDiscardTitle, dialog.Title);
                await SettleAsync(token);
                var confirmation = dialog.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "BackgroundElement");
                Assert.IsLessThanOrEqualTo(420d, confirmation.Bounds.Width);
                Assert.IsLessThan(400d, confirmation.Bounds.Height);
                Assert.IsFalse(result.IsCompleted);
                Invoke(dialog, "CloseButton");
                Assert.AreEqual(Strings.HistoryTagBarSettings, dialog.Title);
                await SettleAsync(token);
                VerifyDialogBounds(dialog, owner);
                Assert.IsTrue(model.Tags.Single(item => item.Name == "Work").IsPinned);
                Invoke(dialog, "CloseButton");
                Invoke(dialog, "PrimaryButton");
                Assert.AreEqual(FAContentDialogResult.None, await result);
                Assert.AreEqual(0, saveCount);
            }
            finally
            {
                model.AcceptChanges();
                dialog.Hide();
                owner.Close();
                CultureInfo.CurrentUICulture = previousCulture;
            }
            return true;
        }, token);
    }

    [TestMethod]
    public async Task ApplyKeepsDraftOnFailureAndCanRetry()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryTagBarSettingsTests));
        await session.Dispatch(async () =>
        {
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            var model = CreateModel();
            var attempts = 0;
            string[] saved = [];
            var dialog = new HistoryTagBarSettingsDialog(model, () =>
            {
                if (++attempts == 1)
                    throw new IOException("save failed");
                saved = model.PinnedNames;
                return Task.CompletedTask;
            });
            var owner = new Window { Width = 850, Height = 650, Content = new Button() };
            try
            {
                owner.Show();
                var result = dialog.ShowAsync(owner);
                Dispatcher.UIThread.RunJobs();
                Toggle(dialog, "Work");
                var apply = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PrimaryButton");
                ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(apply)!).Invoke();
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual("save failed", model.Error);
                Assert.IsFalse(result.IsCompleted);
                Assert.IsEmpty(saved);
                ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(apply)!).Invoke();
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(FAContentDialogResult.Primary, await result);
                CollectionAssert.AreEqual(model.PinnedNames, saved);
            }
            finally
            {
                model.AcceptChanges();
                dialog.Hide();
                owner.Close();
            }
            return true;
        }, token);
    }

    [TestMethod]
    public async Task CancelAfterRevertingChangesClosesWithoutConfirmation()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryTagBarSettingsTests));
        await session.Dispatch(async () =>
        {
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            var model = CreateModel();
            var dialog = new HistoryTagBarSettingsDialog(model, () => throw new AssertFailedException("Cancel saved"));
            var owner = new Window { Width = 850, Height = 650, Content = new Button() };
            try
            {
                owner.Show();
                var result = dialog.ShowAsync(owner);
                Dispatcher.UIThread.RunJobs();
                Toggle(dialog, "Work");
                Toggle(dialog, "Work");
                Invoke(dialog, "CloseButton");
                Assert.AreEqual(FAContentDialogResult.None, await result.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token));
                Assert.AreEqual(Strings.HistoryTagBarSettings, dialog.Title);
            }
            finally
            {
                model.AcceptChanges();
                dialog.Hide();
                owner.Close();
            }
            return true;
        }, TestContext.CancellationTokenSource.Token);
    }

    [TestMethod]
    [DataRow("text", false)]
    [DataRow("padding", false)]
    [DataRow("pin", false)]
    [DataRow("pin", true)]
    public async Task WholePillDragsWithoutTogglingAndPinStillClicks(string part, bool dropInBlankArea)
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryTagBarSettingsTests));
        await session.Dispatch(async () =>
        {
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            var model = CreateModel();
            var item = model.Tags.Single(t => t.Name == "Pinned");
            var dialog = new HistoryTagBarSettingsDialog(model, () => Task.CompletedTask);
            var owner = new Window { Width = 850, Height = 650, Content = new Button() };
            try
            {
                owner.Show();
                _ = dialog.ShowAsync(owner);
                await SettleAsync(token);
                DragPill(owner, dialog, part, new Point(5, 5));
                Assert.IsTrue(item.IsPinned, "Dragging out of the target must not toggle the pin.");
                await SettleAsync(token);
                var area = dialog.FindControl<Border>("_UnpinnedArea")!;
                var target = area.TranslatePoint(new Point(15, area.Bounds.Height - 5), owner)!.Value;
                if (dropInBlankArea)
                {
                    var apply = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PrimaryButton");
                    var footer = apply.TranslatePoint(new Point(15, apply.Bounds.Height / 2), owner)!.Value;
                    DragPill(owner, dialog, part, footer);
                    Assert.IsTrue(item.IsPinned, "The button area must not accept a drop.");
                    await SettleAsync(token);
                    target = apply.TranslatePoint(new Point(15, -50), owner)!.Value;
                    var groupBottom = area.TranslatePoint(new Point(0, area.Bounds.Height), owner)!.Value.Y;
                    Assert.IsGreaterThan(groupBottom + 20, target.Y, "The target must be below the tag group.");
                }
                DragPill(owner, dialog, part, target);
                Assert.IsFalse(item.IsPinned, "Dropping into the unpinned group must unpin.");
                await SettleAsync(token);
                var pin = dialog.GetVisualDescendants().OfType<ToggleButton>().Single(b => b.DataContext == item);
                var point = pin.TranslatePoint(new Point(pin.Bounds.Width / 2, pin.Bounds.Height / 2), owner)!.Value;
                owner.MouseDown(point, MouseButton.Left);
                owner.MouseUp(point, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                Assert.IsTrue(item.IsPinned, "A click without a drag must still toggle the pin.");
                if (dropInBlankArea)
                {
                    await SettleAsync(token);
                    var divider = dialog.FindControl<Border>("_PinDivider")!;
                    var below = divider.TranslatePoint(new Point(15, 4), owner)!.Value;
                    DragPill(owner, dialog, part, below);
                    Assert.IsFalse(item.IsPinned, "Dropping just below the divider must unpin.");
                    await SettleAsync(token);
                    var above = divider.TranslatePoint(new Point(15, -4), owner)!.Value;
                    DragPill(owner, dialog, part, above);
                    Assert.IsTrue(item.IsPinned, "Dropping just above the divider must pin.");
                }
            }
            finally
            {
                model.AcceptChanges();
                dialog.Hide();
                owner.Close();
            }
            return true;
        }, token);
    }

    [TestMethod]
    public async Task DropOverFooterDoesNotUnpinWhenContentScrolls()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryTagBarSettingsTests));
        await session.Dispatch(async () =>
        {
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            var tags = Enumerable.Range(0, 40).ToDictionary(i => $"Tag {i}", _ => 1);
            tags["Pinned"] = 1;
            var model = new HistoryTagBarSettingsViewModel(tags, ["Pinned"]);
            var dialog = new HistoryTagBarSettingsDialog(model, () => Task.CompletedTask);
            var owner = new Window { Width = 300, Height = 400, Content = new Button() };
            try
            {
                owner.Show();
                _ = dialog.ShowAsync(owner);
                await SettleAsync(token);
                var apply = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PrimaryButton");
                var footer = apply.TranslatePoint(new Point(15, apply.Bounds.Height / 2), owner)!.Value;
                DragPill(owner, dialog, "text", footer);
                Assert.IsTrue(model.Tags.Single(t => t.Name == "Pinned").IsPinned);
            }
            finally
            {
                model.AcceptChanges();
                dialog.Hide();
                owner.Close();
            }
            return true;
        }, token);
    }

    [TestMethod]
    public async Task HoldingDragAtEdgeKeepsScrollingUntilReleased()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryTagBarSettingsTests));
        await session.Dispatch(async () =>
        {
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            var tags = new Dictionary<string, int> { ["Pinned"] = 1 };
            foreach (var index in Enumerable.Range(0, 100))
                tags[$"Tag {index}"] = 1;
            var model = new HistoryTagBarSettingsViewModel(tags, tags.Keys.ToArray());
            var dialog = new HistoryTagBarSettingsDialog(model, () => Task.CompletedTask);
            var owner = new Window { Width = 850, Height = 650, Content = new Button() };
            try
            {
                owner.Show();
                _ = dialog.ShowAsync(owner);
                await SettleAsync(token);
                StartPillDrag(owner, dialog, "text");
                var scroll = dialog.FindControl<ScrollViewer>("_TagScroll")!;
                var edge = scroll.TranslatePoint(new Point(15, scroll.Bounds.Height - 4), owner)!.Value;
                owner.MouseMove(edge);
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(300, token);
                Dispatcher.UIThread.RunJobs();
                Assert.IsGreaterThan(12d, scroll.Offset.Y, "A stationary pointer at the edge must keep scrolling.");
                owner.MouseUp(new Point(5, 5), MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                var stoppedOffset = scroll.Offset.Y;
                await Task.Delay(150, token);
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(stoppedOffset, scroll.Offset.Y);
                Assert.IsFalse(model.HasChanges);
            }
            finally
            {
                owner.MouseUp(new Point(5, 5), MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                model.AcceptChanges();
                dialog.Hide();
                owner.Close();
            }
            return true;
        }, token);
    }

    [TestMethod]
    public async Task DragScrollReachesUnpinnedGroupInShortWindow()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryTagBarSettingsTests));
        await session.Dispatch(async () =>
        {
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            var tags = new Dictionary<string, int> { ["Pinned"] = 1 };
            foreach (var index in Enumerable.Range(0, 10))
                tags[$"Tag {index}"] = 1;
            var model = new HistoryTagBarSettingsViewModel(tags, tags.Keys.ToArray());
            var dialog = new HistoryTagBarSettingsDialog(model, () => Task.CompletedTask);
            var owner = new Window { Width = 300, Height = 400, Content = new Button() };
            try
            {
                owner.Show();
                _ = dialog.ShowAsync(owner);
                await SettleAsync(token);
                StartPillDrag(owner, dialog, "text");
                var viewport = dialog.GetVisualDescendants().OfType<ScrollViewer>()
                    .Single(v => v.Name == "ContentScrollViewer");
                var edge = viewport.TranslatePoint(new Point(viewport.Bounds.Width / 2, viewport.Bounds.Height - 4), owner)!.Value;
                owner.MouseMove(edge);
                for (var i = 0; i < 40; i++)
                {
                    await Task.Delay(50, token);
                    Dispatcher.UIThread.RunJobs();
                }
                var divider = dialog.FindControl<Border>("_PinDivider")!;
                Assert.IsLessThan(edge.Y, divider.TranslatePoint(default, owner)!.Value.Y,
                    "Edge scrolling must expose the unpinned group even when the outer viewport clips content.");
                var pinned = dialog.FindControl<ItemsControl>("_PinnedTags")!;
                Assert.IsFalse(pinned.Items.Cast<HistoryTagBarItem>().Any(t => t.Name == "Pinned"),
                    "The preview must follow the stationary pointer across the divider before release.");
                owner.MouseUp(edge, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                Assert.IsFalse(model.Tags.Single(t => t.Name == "Pinned").IsPinned);
            }
            finally
            {
                owner.MouseUp(new Point(5, 5), MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                model.AcceptChanges();
                dialog.Hide();
                owner.Close();
            }
            return true;
        }, token);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DragToUnpinnedAreaShowsGhostBeforeRelease(bool emptyUnpinned)
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryTagBarSettingsTests));
        await session.Dispatch(async () =>
        {
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            var tags = new Dictionary<string, int> { ["Pinned"] = 1 };
            if (!emptyUnpinned)
                tags["Free"] = 1;
            var model = new HistoryTagBarSettingsViewModel(tags, ["Pinned"]);
            var dialog = new HistoryTagBarSettingsDialog(model, () => Task.CompletedTask);
            var owner = new Window { Width = 850, Height = 650, Content = new Button() };
            try
            {
                owner.Show();
                _ = dialog.ShowAsync(owner);
                await SettleAsync(token);
                StartPillDrag(owner, dialog, "text");
                var area = dialog.FindControl<Border>("_UnpinnedArea")!;
                var target = area.TranslatePoint(new Point(area.Bounds.Width / 2, area.Bounds.Height / 2), owner)!.Value;
                owner.MouseMove(target);
                Dispatcher.UIThread.RunJobs();
                var unpinned = dialog.FindControl<ItemsControl>("_UnpinnedTags")!;
                var ghost = unpinned.GetRealizedContainers().Single(c => c.DataContext is HistoryTagBarItem { Name: "Pinned" });
                Assert.AreEqual(0.25, ghost.Opacity);
                Assert.IsTrue(model.PinnedTags.Single().IsPinned);
                Assert.IsFalse(model.HasChanges);
                owner.MouseMove(new Point(5, 5));
                Dispatcher.UIThread.RunJobs();
                Assert.IsFalse(unpinned.GetRealizedContainers().Any(c => c.DataContext is HistoryTagBarItem { Name: "Pinned" }));
                owner.MouseMove(target);
                owner.MouseUp(target, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                Assert.IsEmpty(model.PinnedTags);
                var restored = unpinned.GetRealizedContainers().Single(c => c.DataContext is HistoryTagBarItem { Name: "Pinned" });
                Assert.AreEqual(1d, restored.Opacity);
            }
            finally
            {
                owner.MouseUp(new Point(5, 5), MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                model.AcceptChanges();
                dialog.Hide();
                owner.Close();
            }
            return true;
        }, token);
    }

    private static void DragPill(Window owner, HistoryTagBarSettingsDialog dialog, string part, Point target)
    {
        StartPillDrag(owner, dialog, part);
        owner.MouseMove(target);
        owner.MouseUp(target, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void StartPillDrag(Window owner, HistoryTagBarSettingsDialog dialog, string part)
    {
        owner.UpdateLayout();
        var pill = dialog.GetVisualDescendants().OfType<Border>()
            .Single(b => b.Name == "TagPill" && b.DataContext is HistoryTagBarItem { Name: "Pinned" });
        Control source = part switch
        {
            "text" => pill.GetVisualDescendants().OfType<TextBlock>().First(),
            "pin" => pill.GetVisualDescendants().OfType<ToggleButton>().Single(),
            _ => pill
        };
        var offset = part == "padding" ? new Point(3, 14) : new Point(source.Bounds.Width / 2, source.Bounds.Height / 2);
        var start = source.TranslatePoint(offset, owner)!.Value;
        owner.MouseMove(start);
        owner.MouseDown(start, MouseButton.Left);
        owner.MouseMove(start + new Vector(8, 0));
        Dispatcher.UIThread.RunJobs();
        Assert.IsTrue(owner.GetVisualDescendants().Any(c => c is Control { Name: "TagDragPreview" }),
            $"Dragging from {part} must show the moving pill.");
    }

    private static async Task SettleAsync(CancellationToken token)
    {
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(800, token);
        Dispatcher.UIThread.RunJobs();
    }

    private static void VerifyDialogBounds(HistoryTagBarSettingsDialog dialog, Window owner)
    {
        var background = dialog.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "BackgroundElement");
        var left = background.TranslatePoint(default, owner)!.Value.X;
        Assert.AreEqual(16d, left, 1d);
        Assert.AreEqual(16d, owner.Bounds.Width - left - background.Bounds.Width, 1d);
        var top = background.TranslatePoint(default, owner)!.Value.Y;
        Assert.AreEqual(40d, top, 1d);
        Assert.AreEqual(40d, owner.Bounds.Height - top - background.Bounds.Height, 1d);
        var apply = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PrimaryButton");
        var buttonTop = apply.TranslatePoint(default, owner)!.Value.Y;
        Assert.IsGreaterThanOrEqualTo(top, buttonTop);
        Assert.IsLessThanOrEqualTo(top + background.Bounds.Height, buttonTop + apply.Bounds.Height);
    }

    private static void Invoke(HistoryTagBarSettingsDialog dialog, string name)
    {
        var button = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
        ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(button)!).Invoke();
        Dispatcher.UIThread.RunJobs();
    }

    private static HistoryTagBarSettingsViewModel CreateModel() => new(
        new Dictionary<string, int> { ["Work"] = 2, ["Pinned"] = 1, [new string('W', 60)] = 1, ["Code"] = 5 }, ["Pinned"]);

    private static void Toggle(HistoryTagBarSettingsDialog dialog, string name)
    {
        var button = dialog.GetVisualDescendants().OfType<ToggleButton>()
            .Single(b => b.DataContext is HistoryTagBarItem item && item.Name == name);
        ((IToggleProvider)ControlAutomationPeer.CreatePeerForElement(button)!).Toggle();
        Dispatcher.UIThread.RunJobs();
    }
}
