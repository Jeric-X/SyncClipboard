using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
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
            Application.Current.Styles.Add(new StyleInclude(new Uri("avares://SyncClipboard.Desktop/"))
            {
                Source = new Uri("avares://SyncClipboard.Desktop/Styling/PopupStyles.axaml")
            });
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
                flyout = view.ShowAt(bar.AddTagAnchor);
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

    private static void VerifySearchAndPendingSave(HistoryTagPickerView view, Flyout flyout,
        TaskCompletionSource pendingSave, List<(string, bool)> saved)
    {
        var search = view.FindControl<TextBox>("_TagSearch")!;
        Assert.AreEqual(new CornerRadius(8), view.GetVisualAncestors().OfType<FlyoutPresenter>().Single().CornerRadius);
        var add = view.FindControl<Button>("_AddInput")!;
        var list = view.GetVisualDescendants().OfType<ItemsControl>().Single();
        var scroll = list.GetVisualAncestors().OfType<ScrollViewer>().First();
        Assert.IsLessThanOrEqualTo(200d, scroll.Bounds.Height);
        Assert.IsGreaterThan(scroll.Viewport.Height, scroll.Extent.Height);
        var fullHeight = view.Bounds.Height;
        Assert.IsTrue(search.IsFocused, $"open={flyout.IsOpen}, visible={search.IsEffectivelyVisible}, enabled={search.IsEffectivelyEnabled}");
        search.Text = "cod";
        Dispatcher.UIThread.RunJobs();
        var suggestion = view.GetVisualDescendants().OfType<Button>()
            .Single(b => b.DataContext is HistoryTagSuggestion);
        Assert.AreEqual("code", ((HistoryTagSuggestion)suggestion.DataContext!).Name);
        Assert.IsGreaterThan(100, suggestion.Bounds.Width);
        Assert.IsLessThan(fullHeight, view.Bounds.Height);
        Assert.AreEqual(suggestion.Bounds.Height, scroll.Bounds.Height, 1);
        Assert.IsGreaterThanOrEqualTo(12d, scroll.Bounds.Width - list.Bounds.Width);
        suggestion.Focus();
        suggestion.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.IsTrue(search.IsFocused);
        search.Text = "next";
        Dispatcher.UIThread.RunJobs();
        Assert.IsFalse(add.IsEnabled);
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
        var add = view.FindControl<Button>("_AddInput")!;
        var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().First();
        search.Text = "新标签";
        Dispatcher.UIThread.RunJobs();
        Assert.AreEqual(Strings.HistoryTagsCreateAndAdd, add.Content);
        Assert.IsTrue(add.IsEnabled);
        add.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
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
