using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
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
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia();

    [TestMethod]
    public async Task PinsPreviewInWrappingPillsAndCancelLeavesSavedOrderAlone()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryTagBarSettingsTests));
        await session.Dispatch(async () =>
        {
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
                    .Where(t => t.Text is "Work" or "Pinned").OrderBy(t => t.TranslatePoint(default, owner)!.Value.X).ToArray();
                Assert.AreEqual("Work", ordered[0].Text);
                Toggle(dialog, "Pinned");
                Assert.IsFalse(model.Tags.Single(item => item.Name == "Pinned").IsPinned);
                owner.Width = 300;
                Dispatcher.UIThread.RunJobs();
                owner.UpdateLayout();
                await Task.Delay(800, token);
                Dispatcher.UIThread.RunJobs();
                var panel = (WrapPanel)dialog.FindControl<ItemsControl>("_UnpinnedTags")!.ItemsPanelRoot!;
                Assert.IsGreaterThan(1, panel.Children.Select(c => c.Bounds.Y).Distinct().Count());
                var background = dialog.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "BackgroundElement");
                Assert.IsLessThanOrEqualTo(owner.Bounds.Width, background.Bounds.Width);
                foreach (var toggle in dialog.GetVisualDescendants().OfType<ToggleButton>())
                {
                    var point = toggle.TranslatePoint(default, owner)!.Value;
                    Assert.IsGreaterThanOrEqualTo(0, point.X);
                    Assert.IsLessThanOrEqualTo(owner.Bounds.Width, point.X + toggle.Bounds.Width);
                }
                Invoke(dialog, "CloseButton");
                Assert.AreEqual(Strings.HistoryTagBarDiscardTitle, dialog.Title);
                Assert.IsFalse(result.IsCompleted);
                Invoke(dialog, "CloseButton");
                Assert.AreEqual(Strings.HistoryTagBarSettings, dialog.Title);
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
