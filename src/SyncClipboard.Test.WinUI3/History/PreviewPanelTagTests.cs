using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SyncClipboard.Core.ViewModels.Sub;
using SyncClipboard.Shared.Profiles;
using SyncClipboard.Test.WinUI3.App;
using SyncClipboard.WinUI3.Controls;
using System.Runtime.CompilerServices;
using Windows.Graphics;

namespace SyncClipboard.Test.WinUI3.History;

[TestClass]
public class PreviewPanelTagTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("PlatformWindows")]
    public async Task PreviewSortsTagsAndRefreshesOnRecordChanges()
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
        // Avoid starting clipboard services: this check only renders text records.
        var record = (HistoryRecordVM)RuntimeHelpers.GetUninitializedObject(typeof(HistoryRecordVM));
        record.Type = ProfileType.Text;
        record.Hash = "preview-tags";
        record.Text = string.Empty;
        record.Tags = ["z", "a", "A"];
        var panel = new PreviewPanel { SelectedItem = record };
        var window = new Window { Content = panel };
        window.AppWindow.Move(new PointInt32(-32000, -32000));
        try
        {
            window.Activate();
            var tags = (ItemsControl)panel.FindName("_PreviewTags");
            await WaitForNamesAsync(tags, ["A", "a", "z"], timeout.Token);
            record.Tags = ["z", "b"];
            await WaitForNamesAsync(tags, ["b", "z"], timeout.Token);
            var next = (HistoryRecordVM)RuntimeHelpers.GetUninitializedObject(typeof(HistoryRecordVM));
            next.Type = ProfileType.Text;
            next.Hash = "next-preview-tags";
            next.Text = string.Empty;
            next.Tags = ["c", "a"];
            panel.SelectedItem = next;
            await WaitForNamesAsync(tags, ["a", "c"], timeout.Token);
            record.Tags = ["old"];
            await WaitForNamesAsync(tags, ["a", "c"], timeout.Token);
            panel.SelectedItem = null!;
            await WaitForNamesAsync(tags, [], timeout.Token);
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task WaitForNamesAsync(ItemsControl tags, string[] expected, CancellationToken token)
    {
        while (!tags.IsLoaded || !Descendants<HistoryRecordTagChip>(tags)
            .Select(chip => Descendants<TextBlock>(chip).First().Text).SequenceEqual(expected))
            await Task.Delay(20, token);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match)
            yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            foreach (var item in Descendants<T>(VisualTreeHelper.GetChild(root, i)))
                yield return item;
        }
    }
}
