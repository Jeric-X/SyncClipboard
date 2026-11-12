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
    public async Task CollapsedSelectionSurvivesReflowAndManualScrollingUsesWholeRows()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryTagNavigationTests));
        await session.Dispatch(() =>
        {
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            var tags = Enumerable.Range(0, 8).Select(i => new HistoryTagFilter(i == 0 ? null : $"Tag {i}", 1, false, _ => { })).ToArray();
            var items = new ItemsControl
            {
                ItemsSource = tags,
                ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel { LineSpacing = 6 }),
                ItemTemplate = new FuncDataTemplate<HistoryTagFilter>((_, _) => new Border { Width = 80, Height = 32 })
            };
            var scroll = new ScrollViewer
            {
                Content = items,
                Height = 108,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden
            };
            var navigation = new HistoryTagNavigation();
            items.LayoutUpdated += (_, _) => navigation.KeepSelectionVisible(items, scroll);
            scroll.ScrollChanged += (_, _) => navigation.SnapToRow(items, scroll);
            tags[4].IsSelected = true;
            var window = new Window { Content = scroll, Width = 170, Height = 108 };
            try
            {
                window.Show();
                Layout(window);
                scroll.Height = 32;
                window.Height = 32;
                Layout(window);
                AssertTagVisible(items, scroll, tags[4]);
                Assert.AreEqual(76d, scroll.Offset.Y, 0.5);

                navigation.ScrollByWheel(items, scroll, 1);
                Layout(window);
                Assert.AreEqual(38d, scroll.Offset.Y, 0.5); // Layout must not undo manual scrolling.
                navigation.ScrollByWheel(items, scroll, 0.4);
                Layout(window);
                Assert.AreEqual(38d, scroll.Offset.Y, 0.5);
                navigation.ScrollByWheel(items, scroll, 0.6);
                Layout(window);
                Assert.AreEqual(0d, scroll.Offset.Y, 0.5);
                scroll.LineDown();
                Layout(window);
                Assert.AreEqual(38d, scroll.Offset.Y, 0.5);

                window.Width = 250;
                Layout(window);
                AssertTagVisible(items, scroll, tags[4]);
                window.Width = 90;
                Layout(window);
                AssertTagVisible(items, scroll, tags[4]);

                scroll.Height = 108;
                window.Height = 108;
                Layout(window);
                scroll.Offset = new Vector(0, 51);
                Layout(window);
                Assert.AreEqual(38d, scroll.Offset.Y, 0.5);
                navigation.ScrollByWheel(items, scroll, -100);
                Layout(window);
                Assert.AreEqual(190d, scroll.Offset.Y, 0.5); // The final three rows fit without clipping.
                AssertTagVisible(items, scroll, tags[7]);

                items.ItemsSource = Array.Empty<HistoryTagFilter>();
                Layout(window);
                navigation.ScrollByWheel(items, scroll, 1);
                Assert.IsEmpty(HistoryTagNavigation.GetCandidates(items));
            }
            finally
            {
                window.Close();
            }
            return true;
        }, TestContext.CancellationTokenSource.Token);
    }

    private static void AssertTagVisible(ItemsControl items, ScrollViewer scroll, HistoryTagFilter tag)
    {
        var bounds = HistoryTagNavigation.GetCandidates(items).Single(child => ReferenceEquals(child.DataContext, tag)).Bounds;
        Assert.IsTrue(bounds.Y >= scroll.Offset.Y - 0.5 && bounds.Bottom <= scroll.Offset.Y + scroll.Viewport.Height + 0.5);
    }

    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
