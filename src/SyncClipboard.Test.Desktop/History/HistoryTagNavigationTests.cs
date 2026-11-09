using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Threading;
using FluentAvalonia.Styling;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Desktop.Utilities;

namespace SyncClipboard.Test.Desktop.History;

[TestClass]
[DoNotParallelize]
public class HistoryTagNavigationTests
{
    public TestContext TestContext { get; set; } = null!;

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());

    [TestMethod]
    public async Task CandidatesFollowViewportAfterResizingScrollingAndExpanding()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryTagNavigationTests));
        await session.Dispatch(() =>
        {
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            var tags = Enumerable.Range(0, 8).Select(i => new HistoryTagFilter(i == 0 ? null : $"Tag {i}", 1, false, _ => { })).ToArray();
            var items = new ItemsControl
            {
                ItemsSource = tags,
                ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel()),
                ItemTemplate = new FuncDataTemplate<HistoryTagFilter>((_, _) => new Border { Width = 80, Height = 32 })
            };
            var scroll = new ScrollViewer
            {
                Content = items,
                Height = 32,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden
            };
            var window = new Window { Content = scroll, Width = 170, Height = 32 };
            try
            {
                window.Show();
                Layout(window);
                AssertCandidates(items, scroll, false, tags[..2]);
                AssertCandidates(items, scroll, true, tags);
                window.Width = 250;
                Layout(window);
                AssertCandidates(items, scroll, false, tags[..3]);
                scroll.Offset = new Vector(0, 32);
                Layout(window);
                AssertCandidates(items, scroll, false, tags[3..6]);
                scroll.Offset = default;
                window.Width = 90;
                Layout(window);
                AssertCandidates(items, scroll, false, tags[..1]);
                AssertCandidates(items, scroll, true, tags);
                items.ItemsSource = Array.Empty<HistoryTagFilter>();
                Layout(window);
                Assert.IsEmpty(HistoryTagNavigation.GetCandidates(items, scroll, false));
            }
            finally
            {
                window.Close();
            }
            return true;
        }, TestContext.CancellationTokenSource.Token);
    }

    private static void AssertCandidates(ItemsControl items, ScrollViewer scroll, bool expanded, HistoryTagFilter[] expected) =>
        CollectionAssert.AreEqual(expected, HistoryTagNavigation.GetCandidates(items, scroll, expanded)
            .Select(child => (HistoryTagFilter)child.DataContext!).ToArray());

    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
