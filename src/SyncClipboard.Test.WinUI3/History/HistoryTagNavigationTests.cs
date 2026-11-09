using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Test.WinUI3.App;
using SyncClipboard.WinUI3.Utilities;
using Windows.Graphics;

namespace SyncClipboard.Test.WinUI3.History;

[TestClass]
public class HistoryTagNavigationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("PlatformWindows")]
    public async Task CandidatesFollowViewportAfterResizingScrollingAndExpanding()
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
        var token = timeout.Token;
        var tags = Enumerable.Range(0, 8).Select(i => new HistoryTagFilter(i == 0 ? null : $"Tag {i}", 1, false, _ => { })).ToArray();
        var items = (ItemsControl)XamlReader.Load("""
            <ItemsControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                          xmlns:toolkit="using:CommunityToolkit.WinUI.Controls">
                <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate><toolkit:WrapPanel /></ItemsPanelTemplate>
                </ItemsControl.ItemsPanel>
                <ItemsControl.ItemTemplate>
                    <DataTemplate><Border Width="80" Height="32" /></DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """);
        items.ItemsSource = tags;
        var scroll = new ScrollViewer
        {
            Content = items,
            Width = 170,
            Height = 32,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden
        };
        var root = new Grid();
        root.Children.Add(scroll);
        var window = new Window { Content = root };
        window.AppWindow.Move(new PointInt32(-32000, -32000));
        try
        {
            window.Activate();
            await WaitUntilAsync(() => items.IsLoaded && items.ItemsPanelRoot?.Children.Count == tags.Length, token);
            root.UpdateLayout();
            AssertCandidates(items, scroll, false, tags[..2]);
            AssertCandidates(items, scroll, true, tags);
            scroll.Width = 250;
            root.UpdateLayout();
            AssertCandidates(items, scroll, false, tags[..3]);
            scroll.ChangeView(null, 32, null, true);
            await WaitUntilAsync(() => Math.Abs(scroll.VerticalOffset - 32) < 0.5, token);
            AssertCandidates(items, scroll, false, tags[3..6]);
            scroll.ChangeView(null, 0, null, true);
            await WaitUntilAsync(() => scroll.VerticalOffset < 0.5, token);
            scroll.Width = 90;
            root.UpdateLayout();
            AssertCandidates(items, scroll, false, tags[..1]);
            AssertCandidates(items, scroll, true, tags);
            items.ItemsSource = Array.Empty<HistoryTagFilter>();
            root.UpdateLayout();
            Assert.IsEmpty(HistoryTagNavigation.GetCandidates(items, scroll, false));
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static void AssertCandidates(ItemsControl items, ScrollViewer scroll, bool expanded, HistoryTagFilter[] expected) =>
        CollectionAssert.AreEqual(expected, HistoryTagNavigation.GetCandidates(items, scroll, expanded)
            .Select(child => (HistoryTagFilter)child.DataContext).ToArray());

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken token)
    {
        while (!condition())
            await Task.Delay(20, token);
    }
}
