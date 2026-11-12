using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.Styling;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Desktop.Views;

namespace SyncClipboard.Test.Desktop.History;

[TestClass]
[DoNotParallelize]
public class HistoryTagPickerTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia();

    [TestMethod]
    public async Task PickerFlyout_FocusesInputFiltersAndAddsWithoutClosing()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryTagPickerTests));
        await session.Dispatch(() =>
        {
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            var saved = new List<(string, bool)>();
            var pendingSave = new TaskCompletionSource();
            var picker = new HistoryTagPickerViewModel(["work", "code", .. Enumerable.Range(1, 20).Select(i => $"tag{i}")], ["work"], (name, add) =>
            {
                saved.Add((name, add));
                return pendingSave.Task;
            });
            var view = new HistoryTagPickerView(picker);
            var bar = new RecordControlBar();
            var owner = new Window { Width = 800, Height = 600, Content = bar };
            Flyout? flyout = null;
            try
            {
                owner.Show();
                // Keep the real control-bar styles in the popup's placement context.
                bar.Record = (SyncClipboard.Core.ViewModels.Sub.HistoryRecordVM)
                    System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(SyncClipboard.Core.ViewModels.Sub.HistoryRecordVM));
                owner.UpdateLayout();
                flyout = view.ShowAt(bar.FindControl<Button>("_AddTagButton")!);
                Dispatcher.UIThread.RunJobs();
                VerifySearchAndPendingSave(view, flyout, pendingSave, saved);
                VerifyContinuousEditing(view, flyout, saved);
            }
            finally
            {
                flyout?.Hide();
                owner.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task KeyboardEditing_NavigatesTogglesKeepsFocusAndClosesOnlyTheFlyout()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryTagPickerTests));
        await session.Dispatch(() =>
        {
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            var saved = new List<(string, bool)>();
            var picker = new HistoryTagPickerViewModel(["code", "work", .. Enumerable.Range(1, 20).Select(i => $"tag{i:D2}")], [], (name, add) =>
            {
                saved.Add((name, add));
                return Task.CompletedTask;
            });
            var view = new HistoryTagPickerView(picker);
            var anchor = new Button { Content = "record" };
            var owner = new Window { Width = 800, Height = 600, Content = anchor };
            Flyout? flyout = null;
            try
            {
                owner.Show();
                owner.UpdateLayout();
                flyout = view.ShowAt(anchor);
                Dispatcher.UIThread.RunJobs();
                var search = view.FindControl<TextBox>("_TagSearch")!;
                var popup = TopLevel.GetTopLevel(view)!;
                Assert.IsTrue(search.IsFocused);
                Assert.AreEqual("code", picker.SelectedSuggestion?.Name);
                Press(popup, Key.Space);
                CollectionAssert.AreEqual(new[] { ("code", true) }, saved);
                Assert.IsTrue(search.IsFocused);
                Press(popup, Key.Enter);
                CollectionAssert.AreEqual(new[] { ("code", true), ("code", false) }, saved);
                Press(popup, Key.Tab);
                Assert.AreEqual("tag01", picker.SelectedSuggestion?.Name);
                Press(popup, Key.Tab, RawInputModifiers.Shift);
                Assert.AreEqual("code", picker.SelectedSuggestion?.Name);
                Press(popup, Key.Tab, RawInputModifiers.Shift);
                Assert.AreEqual("work", picker.SelectedSuggestion?.Name);
                Press(popup, Key.Tab);
                Assert.AreEqual("code", picker.SelectedSuggestion?.Name);
                Assert.IsTrue(search.IsFocused);
                Assert.HasCount(1, picker.Suggestions.Where(tag => tag.IsCurrent));
                Press(popup, Key.Down);
                Assert.AreEqual("tag01", picker.SelectedSuggestion?.Name);
                Press(popup, Key.Up);
                Assert.AreEqual("code", picker.SelectedSuggestion?.Name);

                // Existing tags keep the filter for both pointer and keyboard assignment.
                search.Text = "co";
                Dispatcher.UIThread.RunJobs();
                Suggestion(view, "code").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(("code", true), saved[^1]);
                Assert.AreEqual("co", search.Text);
                Suggestion(view, "code").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(("code", false), saved[^1]);
                Assert.AreEqual("co", search.Text);
                search.Text = "code";
                Dispatcher.UIThread.RunJobs();
                Press(popup, Key.Enter);
                Assert.AreEqual(("code", true), saved[^1]);
                Assert.AreEqual("code", search.Text);
                Assert.IsTrue(search.IsFocused);

                // Enter toggles the highlighted existing tag while the new input candidate stays first.
                search.Text = "tag";
                Dispatcher.UIThread.RunJobs();
                Press(popup, Key.Tab);
                Assert.AreEqual("tag01", picker.SelectedSuggestion?.Name);
                Press(popup, Key.Enter);
                Assert.AreEqual(("tag01", true), saved[^1]);
                Assert.AreEqual("tag", picker.Search);
                Press(popup, Key.Enter);
                Assert.AreEqual(("tag01", false), saved[^1]);
                Assert.AreEqual("tag", picker.Search);
                Press(popup, Key.Tab, RawInputModifiers.Shift);
                Assert.AreEqual("tag", picker.SelectedSuggestion?.Name);
                Assert.IsNotNull(picker.SelectedSuggestion);
                Assert.IsTrue(picker.SelectedSuggestion.IsNew);
                Press(popup, Key.Enter);
                Assert.AreEqual(("tag", true), saved[^1]);
                Assert.IsEmpty(picker.Search);
                Assert.AreEqual("tag", picker.SelectedSuggestion?.Name);
                Assert.IsTrue(search.IsFocused);

                popup.KeyTextInput("新标签");
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual("新标签", picker.SelectedSuggestion?.Name);
                Assert.IsNotNull(picker.SelectedSuggestion);
                Assert.IsTrue(picker.SelectedSuggestion.IsNew);
                Press(popup, Key.Tab);
                Assert.IsTrue(search.IsFocused);
                Press(popup, Key.Enter);
                Assert.AreEqual(("新标签", true), saved[^1]);
                Assert.IsEmpty(search.Text!);
                Assert.AreEqual("新标签", picker.SelectedSuggestion?.Name);
                Assert.IsTrue(search.IsFocused);
                var scroll = view.FindControl<ItemsControl>("_Suggestions")!
                    .GetVisualAncestors().OfType<ScrollViewer>().First();
                Assert.IsGreaterThan(0d, scroll.Offset.Y);
                var added = Suggestion(view, "新标签");
                var position = added.TranslatePoint(default, scroll)!.Value;
                Assert.IsGreaterThanOrEqualTo(0d, position.Y);
                Assert.IsLessThanOrEqualTo(scroll.Viewport.Height + 1, position.Y + added.Bounds.Height);
                Press(popup, Key.Space);
                Assert.AreEqual(("新标签", false), saved[^1]);
                Assert.IsTrue(search.IsFocused);
                Press(popup, Key.Escape);
                Assert.IsFalse(flyout.IsOpen);
                Assert.IsTrue(owner.IsVisible);
            }
            finally
            {
                flyout?.Hide();
                owner.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task PendingKeyboardSave_RejectsRepeatsAndDoesNotStealFocusFromSearch()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryTagPickerTests));
        await session.Dispatch(() =>
        {
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            var pending = new TaskCompletionSource();
            var saves = 0;
            var picker = new HistoryTagPickerViewModel(["code", "work"], [], (_, _) =>
            {
                saves++;
                return pending.Task;
            });
            var view = new HistoryTagPickerView(picker);
            var anchor = new Button { Content = "record" };
            var owner = new Window { Width = 800, Height = 600, Content = anchor };
            Flyout? flyout = null;
            try
            {
                owner.Show();
                owner.UpdateLayout();
                flyout = view.ShowAt(anchor);
                Dispatcher.UIThread.RunJobs();
                var popup = TopLevel.GetTopLevel(view)!;
                var search = view.FindControl<TextBox>("_TagSearch")!;
                Press(popup, Key.Space);
                Assert.IsTrue(search.IsFocused);
                Press(popup, Key.Space);
                Assert.AreEqual(1, saves);
                Assert.IsTrue(search.Focus());
                search.Text = "work";
                Dispatcher.UIThread.RunJobs();
                pending.SetException(new InvalidOperationException("save failed"));
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual("save failed", picker.Error);
                Assert.IsTrue(search.IsFocused);
                Assert.AreEqual("work", search.Text);
                Assert.IsFalse(picker.Suggestions.Single().IsAssigned);
                Press(popup, Key.Escape);
                Assert.IsFalse(flyout.IsOpen);
                Assert.IsTrue(view.ClosedByKeyboard);
            }
            finally
            {
                flyout?.Hide();
                owner.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task Search_AllowsNativeTextInputAfterKeyDown()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryTagPickerTests));
        await session.Dispatch(() =>
        {
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            var saved = new List<string>();
            var picker = new HistoryTagPickerViewModel(["code", "work"], [], (name, _) =>
            {
                saved.Add(name);
                return Task.CompletedTask;
            });
            var view = new HistoryTagPickerView(picker);
            var anchor = new Button { Content = "record" };
            var owner = new Window { Width = 800, Height = 600, Content = anchor };
            Flyout? flyout = null;
            try
            {
                owner.Show();
                owner.UpdateLayout();
                flyout = view.ShowAt(anchor);
                Dispatcher.UIThread.RunJobs();
                var popup = TopLevel.GetTopLevel(view)!;
                // Headless separates key and text input. Model the native host's decision to
                // continue text input only when the key-down was not consumed by the application.
                KeyEventArgs? keyDown = null;
                view.AddHandler(InputElement.KeyDownEvent, (_, args) => keyDown = args,
                    RoutingStrategies.Bubble, handledEventsToo: true);
                var search = view.FindControl<TextBox>("_TagSearch")!;
                search.Text = "pending";
                var presenter = search.GetVisualDescendants().OfType<TextPresenter>().Single();
                presenter.PreeditText = "中文";
                Press(popup, Key.Enter);
                Assert.IsEmpty(saved);
                Assert.IsTrue(flyout.IsOpen);
                presenter.PreeditText = null;
                search.Text = "";
                Press(popup, Key.C);
                Assert.IsNotNull(keyDown);
                if (!keyDown.Handled)
                    popup.KeyTextInput("c");
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual("c", view.FindControl<TextBox>("_TagSearch")!.Text);
                Assert.AreEqual("c", picker.Search);
                Assert.AreEqual("c", picker.Suggestions[0].Name);
                Assert.AreEqual("code", picker.Suggestions[1].Name);
                Press(popup, Key.Space);
                Assert.IsFalse(keyDown!.Handled, "Spaces inside tag names must reach native text input.");
                popup.KeyTextInput(" ");
                Assert.AreEqual("c ", picker.Search);
                Press(popup, Key.Back);
                Press(popup, Key.Back);
                Assert.IsEmpty(picker.Search);
                Assert.HasCount(2, picker.Suggestions);
                Assert.IsTrue(flyout.IsOpen);
            }
            finally
            {
                flyout?.Hide();
                owner.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    private static Button Suggestion(HistoryTagPickerView view, string name) =>
        view.GetVisualDescendants().OfType<Button>().Single(button =>
            button.DataContext is HistoryTagSuggestion suggestion && suggestion.Name == name);

    private static void Press(TopLevel root, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        root.KeyPress(key, modifiers, PhysicalKey.None, null);
        root.KeyRelease(key, modifiers, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static void VerifySearchAndPendingSave(HistoryTagPickerView view, Flyout flyout,
        TaskCompletionSource pendingSave, List<(string, bool)> saved)
    {
        var search = view.FindControl<TextBox>("_TagSearch")!;
        var list = view.GetVisualDescendants().OfType<ItemsControl>().Single();
        var scroll = list.GetVisualAncestors().OfType<ScrollViewer>().First();
        Assert.IsLessThanOrEqualTo(200d, scroll.Bounds.Height);
        Assert.IsGreaterThan(scroll.Viewport.Height, scroll.Extent.Height);
        var fullHeight = view.Bounds.Height;
        Assert.IsTrue(search.IsFocused, $"open={flyout.IsOpen}, visible={search.IsEffectivelyVisible}, enabled={search.IsEffectivelyEnabled}");
        search.Text = "cod";
        Dispatcher.UIThread.RunJobs();
        var suggestion = view.GetVisualDescendants().OfType<Button>()
            .Single(b => b.DataContext is HistoryTagSuggestion { Name: "code" });
        Assert.AreEqual("code", ((HistoryTagSuggestion)suggestion.DataContext!).Name);
        Assert.IsGreaterThan(100, suggestion.Bounds.Width);
        Assert.IsLessThan(fullHeight, view.Bounds.Height);
        Assert.AreEqual(suggestion.Bounds.Height * 2, scroll.Bounds.Height, 1);
        Assert.IsGreaterThanOrEqualTo(12d, scroll.Bounds.Width - list.Bounds.Width);
        suggestion.Focus();
        suggestion.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.IsTrue(search.IsFocused);
        search.Text = "next";
        Dispatcher.UIThread.RunJobs();
        var lostFocus = 0;
        search.LostFocus += (_, _) => lostFocus++;
        pendingSave.SetResult();
        Dispatcher.UIThread.RunJobs();
        Assert.AreEqual("next", search.Text);
        Assert.IsTrue(search.IsFocused);
        Assert.AreEqual(0, lostFocus);
        CollectionAssert.AreEqual(new[] { ("code", true) }, saved);
        Assert.IsTrue(flyout.IsOpen);
    }

    private static void VerifyContinuousEditing(HistoryTagPickerView view, Flyout flyout, List<(string, bool)> saved)
    {
        var picker = view.ViewModel;
        var search = view.FindControl<TextBox>("_TagSearch")!;
        var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().First();
        search.Text = "新标签";
        Dispatcher.UIThread.RunJobs();
        var candidate = Suggestion(view, "新标签");
        var badge = candidate.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == Strings.HistoryTagsNew);
        Assert.IsTrue(badge.IsEffectivelyVisible);
        candidate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        CollectionAssert.AreEqual(new[] { ("code", true), ("新标签", true) }, saved);
        Assert.IsTrue(flyout.IsOpen);
        var assigned = view.GetVisualDescendants().OfType<Button>()
            .Single(b => b.DataContext is HistoryTagSuggestion { Name: "work" });
        Assert.IsTrue(assigned.IsEnabled);
        assigned.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.AreEqual(("work", false), saved[^1]);
        Assert.IsFalse(picker.Suggestions.Single(t => t.Name == "work").IsAssigned);
        Assert.IsTrue(flyout.IsOpen);
        Assert.IsTrue(search.IsFocused, $"open={flyout.IsOpen}, visible={search.IsEffectivelyVisible}, enabled={search.IsEffectivelyEnabled}");
        scroll.Offset = new Vector(0, scroll.Extent.Height);
        Dispatcher.UIThread.RunJobs();
        var offset = scroll.Offset;
        var lastTag = view.GetVisualDescendants().OfType<Button>()
            .Single(b => b.DataContext is HistoryTagSuggestion { Name: "tag20" });
        var checkmark = lastTag.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "✓");
        Assert.IsFalse(checkmark.IsVisible);
        lastTag.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.IsTrue(checkmark.IsVisible);
        Assert.AreEqual(offset, scroll.Offset);
        lastTag.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.IsFalse(checkmark.IsVisible);
        Assert.AreEqual(offset, scroll.Offset);
    }
}
