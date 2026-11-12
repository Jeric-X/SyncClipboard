using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.ViewModels.Sub;
using SyncClipboard.Shared.Profiles;
using SyncClipboard.Test.WinUI3.App;
using SyncClipboard.WinUI3.Controls;
using System.Runtime.CompilerServices;
using Windows.Foundation;
using Windows.Graphics;

namespace SyncClipboard.Test.WinUI3.History;

[TestClass]
public class HistoryInlineTagsTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("PlatformWindows")]
    public async Task OverflowShowsHiddenTagsAndRemovalRequestsTheDisplayedTag()
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
        var record = new TagBindingSource();
        var names = Enumerable.Range(0, 16).Select(i => $"tag{i:D2}").ToArray();
        record.Tags = names;
        // The panel only subscribes to selection notifications in this UI test.
        var viewModel = (HistoryViewModel)RuntimeHelpers.GetUninitializedObject(typeof(HistoryViewModel));
        Console.Error.WriteLine("Creating the history row and its overflow button.");
        var panel = new HistoryInlineTagsView
        {
            DataContext = record,
            ViewModel = viewModel,
            OverflowTemplate = (DataTemplate)XamlReader.Load(
                """<DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><Button Width="28" Height="22" /></DataTemplate>""")
        };
        panel.SetBinding(HistoryInlineTagsView.TagsProperty, new Binding
        {
            Source = record,
            Path = new PropertyPath(nameof(TagBindingSource.PreviewTags)),
            Mode = BindingMode.OneWay
        });
        panel.SetBinding(HistoryInlineTagsView.PinnedTagsProperty, new Binding
        {
            Source = record,
            Path = new PropertyPath(nameof(TagBindingSource.PinnedTags)),
            Mode = BindingMode.OneWay
        });
        var search = new TextBox();
        var row = new DockPanel { Width = 360, LastChildFill = true };
        DockPanel.SetDock(panel, Dock.Right);
        row.Children.Add(panel);
        row.Children.Add(new Border { Height = 40 });
        var scroll = new ScrollViewer
        {
            Height = 100,
            Content = new StackPanel { Children = { row, new Border { Height = 400 } } }
        };
        var window = new Window { Content = new StackPanel { Children = { search, scroll } } };
        window.AppWindow.Move(new PointInt32(-32000, -32000));
        try
        {
            Console.Error.WriteLine("Activating the window and laying out the row.");
            window.Activate();
            await WaitUntilAsync(() => panel.Children.OfType<Button>().Any(), timeout.Token);
            Assert.IsTrue(search.Focus(FocusState.Programmatic));
            await DrainInputAsync(panel);
            await VerifyShortTagsAsync(record, panel, timeout.Token);
            await VerifyLongTagAsync(record, panel, row, timeout.Token);
            record.Tags = names;
            await WaitUntilAsync(() => Equals(panel.Children.OfType<Button>().SingleOrDefault()?.Content,
                $"+{names.Length - VisibleChipNames(panel).Length}"), timeout.Token);
            var trigger = panel.Children.OfType<Button>().Single();
            var wideCount = VisibleChipNames(panel).Length;
            Assert.IsGreaterThan(0, wideCount);
            Assert.AreEqual(40d, row.ActualHeight);
            row.Width = 190;
            await WaitUntilAsync(() => panel.ActualWidth <= 95 && panel.Children.OfType<HistoryRecordTagChip>().Count() < wideCount, timeout.Token);
            var narrowNames = VisibleChipNames(panel);
            CollectionAssert.AreEqual(names[..narrowNames.Length], narrowNames);
            Assert.AreEqual(40d, row.ActualHeight);
            var narrowTrigger = panel.Children.OfType<Button>().Single();
            AssertInPanel(panel, narrowTrigger);
            Assert.AreEqual($"+{names.Length - narrowNames.Length}", narrowTrigger.Content);
            row.Width = 360;
            await WaitUntilAsync(() => panel.Children.OfType<HistoryRecordTagChip>().Count() == wideCount, timeout.Token);
            var inlineNames = VisibleChipNames(panel);
            var visible = inlineNames.Length;
            Assert.IsGreaterThan(0, visible);
            CollectionAssert.AreEqual(names[..visible], inlineNames);
            Assert.AreEqual($"+{names.Length - visible}", trigger.Content);
            Console.Error.WriteLine("Opening the tag flyout.");
            Invoke(trigger);
            await WaitUntilAsync(() => FindPopupTags(panel).Any(), timeout.Token);
            var presenter = VisualTreeHelper.GetOpenPopupsForXamlRoot(panel.XamlRoot)
                .SelectMany(p => Descendants<FlyoutPresenter>(p.Child)).Single();
            Assert.AreEqual((CornerRadius)Application.Current.Resources["OverlayCornerRadius"], presenter.CornerRadius);
            Assert.AreSame(search, FocusManager.GetFocusedElement(panel.XamlRoot));
            var hidden = FindPopupTags(panel).ToArray();
            CollectionAssert.AreEqual(names[visible..], RenderedNames(panel));

            record.PinnedTags = ["missing", "tag15", "tag14"];
            string[] pinnedOrder = ["tag15", "tag14", .. names[..14]];
            await WaitUntilAsync(() => VisibleChipNames(panel).Concat(RenderedNames(panel)).SequenceEqual(pinnedOrder), timeout.Token);
            record.PinnedTags = ["tag14", "tag15"];
            pinnedOrder = ["tag14", "tag15", .. names[..14]];
            await WaitUntilAsync(() => VisibleChipNames(panel).Concat(RenderedNames(panel)).SequenceEqual(pinnedOrder), timeout.Token);
            record.Tags = names.Reverse().ToArray();
            await WaitUntilAsync(() => VisibleChipNames(panel).Concat(RenderedNames(panel)).SequenceEqual(pinnedOrder), timeout.Token);
            record.PinnedTags = [];
            await WaitUntilAsync(() => VisibleChipNames(panel).Concat(RenderedNames(panel)).SequenceEqual(names), timeout.Token);
            record.Tags = names;
            await DrainInputAsync(panel);
            hidden = FindPopupTags(panel).ToArray();

            HistoryPreviewTag? requested = null;
            hidden[0].RemoveRequested += (_, tag) => requested = tag;
            Invoke(Descendants<Button>(hidden[0]).Single());
            await WaitUntilAsync(() => requested is not null, timeout.Token);
            Assert.IsNotNull(requested);
            Assert.AreEqual(record.Key, requested.Record);
            Assert.AreEqual(names[visible], requested.Name);
            record.Tags = names.Where(n => n != requested.Name).ToArray();
            await WaitUntilAsync(() => FindPopupTags(panel).Count() == hidden.Length - 1, timeout.Token);
            CollectionAssert.AreEqual(record.Tags[visible..], RenderedNames(panel));
            scroll.ChangeView(null, 150, null, disableAnimation: true);
            await WaitUntilAsync(() => scroll.VerticalOffset >= 150 && !FindPopupTags(panel).Any(), timeout.Token);
            Assert.IsTrue(trigger.IsLoaded);
            Invoke(trigger);
            await DrainInputAsync(panel);
            Assert.IsFalse(FindPopupTags(panel).Any());
            record.Tags = [];
            await WaitUntilAsync(() => !FindPopupTags(panel).Any(), timeout.Token);
            Assert.IsEmpty(panel.Children);
        }
        finally
        {
            window.Close();
        }
    }

    public sealed class TagBindingSource : ObservableObject
    {
        public HistoryRecordKey Key { get; } = new(ProfileType.Text, "inline-tag-ui-test");
        private string[] _tags = [];
        private string[] _pinnedTags = [];

        public string[] PinnedTags
        {
            get => _pinnedTags;
            set => SetProperty(ref _pinnedTags, value);
        }

        public string[] Tags
        {
            get => _tags;
            set
            {
                if (SetProperty(ref _tags, value))
                    OnPropertyChanged(nameof(PreviewTags));
            }
        }

        public IEnumerable<HistoryPreviewTag> PreviewTags => Tags.Select(name => new HistoryPreviewTag(Key, name));
    }

    private static async Task VerifyShortTagsAsync(TagBindingSource record, HistoryInlineTagsView panel, CancellationToken token)
    {
        string[][] cases = [["123"], ["12", "123"], ["123"]];
        foreach (var names in cases)
        {
            record.Tags = names;
            await WaitUntilAsync(() => VisibleChipNames(panel).SequenceEqual(names), token);
            await DrainInputAsync(panel);
            Assert.IsFalse(panel.Children.OfType<Button>().Any());
            foreach (var chip in panel.Children.OfType<HistoryRecordTagChip>())
            {
                var label = Descendants<TextBlock>(chip).First();
                Assert.IsFalse(label.IsTextTrimmed,
                    $"Short tag {label.Text} was trimmed: label={label.ActualWidth}, chip={chip.ActualWidth}, " +
                    $"column={panel.ActualWidth}, scale={panel.XamlRoot.RasterizationScale}.");
            }
        }
    }

    private static async Task VerifyLongTagAsync(TagBindingSource record, HistoryInlineTagsView panel, DockPanel row, CancellationToken token)
    {
        const string first = "12312312141515155555555555555555555";
        record.Tags = [first];
        await WaitUntilAsync(() => !panel.Children.OfType<Button>().Any() && VisibleChipNames(panel).SequenceEqual([first]), token);
        var height = row.ActualHeight;

        record.Tags = [first, "33"];
        await WaitUntilAsync(() => Equals(panel.Children.OfType<Button>().SingleOrDefault()?.Content, "+1"), token);
        CollectionAssert.AreEqual(new[] { first }, VisibleChipNames(panel));
        var chip = panel.Children.OfType<HistoryRecordTagChip>().Single();
        var label = Descendants<TextBlock>(chip).First();
        Assert.IsTrue(label.ActualWidth > 0 && label.IsTextTrimmed);
        var more = panel.Children.OfType<Button>().Single();
        var remove = Descendants<Button>(chip).Single();
        var removeEnd = remove.TransformToVisual(more).TransformPoint(new Point(remove.ActualWidth, 0));
        Assert.IsLessThanOrEqualTo(1d, removeEnd.X, "The remove button must fit before the overflow button.");
        Assert.AreEqual(height, row.ActualHeight);
        Invoke(more);
        await WaitUntilAsync(() => RenderedNames(panel).SequenceEqual(["33"]), token);
        record.Tags = [first];
        await WaitUntilAsync(() => !FindPopupTags(panel).Any() && !panel.Children.OfType<Button>().Any(), token);
    }

    private static string[] VisibleChipNames(HistoryInlineTagsView panel)
    {
        var names = new List<string>();
        foreach (var chip in panel.Children.OfType<HistoryRecordTagChip>())
        {
            AssertInPanel(panel, chip);
            names.Add(Descendants<TextBlock>(chip).First().Text);
        }
        return names.ToArray();
    }

    private static void AssertInPanel(HistoryInlineTagsView panel, FrameworkElement child)
    {
        var bounds = child.TransformToVisual(panel).TransformBounds(new Rect(0, 0, child.ActualWidth, child.ActualHeight));
        Assert.IsTrue(bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= -1 && bounds.Top >= -1 &&
            bounds.Right <= panel.ActualWidth + 1 && bounds.Bottom <= panel.ActualHeight + 1, bounds.ToString());
    }

    private static string[] RenderedNames(FrameworkElement owner) =>
        FindPopupTags(owner).Select(chip => Descendants<TextBlock>(chip).First().Text).ToArray();

    private static Task DrainInputAsync(FrameworkElement element)
    {
        var idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.IsTrue(element.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => idle.SetResult()));
        return idle.Task;
    }

    private static IEnumerable<HistoryRecordTagChip> FindPopupTags(FrameworkElement owner) =>
        VisualTreeHelper.GetOpenPopupsForXamlRoot(owner.XamlRoot).SelectMany(p => Descendants<HistoryRecordTagChip>(p.Child));

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

    private static void Invoke(Button button) =>
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken token)
    {
        while (!condition())
            await Task.Delay(20, token);
    }
}
