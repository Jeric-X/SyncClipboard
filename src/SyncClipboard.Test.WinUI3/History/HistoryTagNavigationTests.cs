using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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
    public async Task CollapsedSelectionSurvivesReflowAndManualScrollingUsesWholeRows()
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
                    <ItemsPanelTemplate><toolkit:WrapPanel VerticalSpacing="6" /></ItemsPanelTemplate>
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
            Height = 108,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden
        };
        var navigation = new HistoryTagNavigation();
        items.LayoutUpdated += (_, _) => navigation.KeepSelectionVisible(items, scroll);
        scroll.ViewChanged += (_, e) =>
        {
            if (!e.IsIntermediate)
                navigation.SnapToRow(items, scroll);
        };
        tags[4].IsSelected = true;
        var root = new Grid();
        root.Children.Add(scroll);
        var window = new Window { Content = root };
        window.AppWindow.Move(new PointInt32(-32000, -32000));
        try
        {
            window.Activate();
            await WaitUntilAsync(() => items.IsLoaded && items.ItemsPanelRoot?.Children.Count == tags.Length, token);
            root.UpdateLayout();
            scroll.Height = 32;
            root.UpdateLayout();
            await WaitUntilAsync(() => IsVisible(items, scroll, tags[4]), token);
            Assert.AreEqual(76d, scroll.VerticalOffset, 0.5);

            navigation.ScrollByWheel(items, scroll, 1);
            await WaitUntilAsync(() => Math.Abs(scroll.VerticalOffset - 38) < 0.5, token);
            root.UpdateLayout();
            Assert.AreEqual(38d, scroll.VerticalOffset, 0.5);
            navigation.ScrollByWheel(items, scroll, 0.4);
            root.UpdateLayout();
            Assert.AreEqual(38d, scroll.VerticalOffset, 0.5);
            navigation.ScrollByWheel(items, scroll, 0.6);
            await WaitUntilAsync(() => scroll.VerticalOffset < 0.5, token);

            scroll.Width = 250;
            root.UpdateLayout();
            await WaitUntilAsync(() => IsVisible(items, scroll, tags[4]), token);
            scroll.Width = 90;
            root.UpdateLayout();
            await WaitUntilAsync(() => IsVisible(items, scroll, tags[4]), token);

            scroll.Height = 108;
            root.UpdateLayout();
            await WaitUntilAsync(() => Math.Abs(scroll.ViewportHeight - 108) < 0.5, token);
            scroll.ChangeView(null, 51, null, true);
            await WaitUntilAsync(() => Math.Abs(scroll.VerticalOffset - 38) < 0.5, token);
            navigation.ScrollByWheel(items, scroll, -100);
            await WaitUntilAsync(() => Math.Abs(scroll.VerticalOffset - 190) < 0.5, token);
            Assert.IsTrue(IsVisible(items, scroll, tags[7]));

            items.ItemsSource = Array.Empty<HistoryTagFilter>();
            root.UpdateLayout();
            navigation.ScrollByWheel(items, scroll, 1);
            Assert.IsEmpty(HistoryTagNavigation.GetCandidates(items));
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static bool IsVisible(ItemsControl items, ScrollViewer scroll, HistoryTagFilter tag)
    {
        var child = HistoryTagNavigation.GetCandidates(items).Single(child => ReferenceEquals(child.DataContext, tag));
        var bounds = LayoutInformation.GetLayoutSlot(child);
        return bounds.Y >= scroll.VerticalOffset - 0.5 && bounds.Bottom <= scroll.VerticalOffset + scroll.ViewportHeight + 0.5;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken token)
    {
        while (!condition())
            await Task.Delay(20, token);
    }
}
