using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.Styling;
using SyncClipboard.Core.I18n;
using SyncClipboard.Desktop.Controls;
using System.Globalization;

namespace SyncClipboard.Test.Desktop.History;

[TestClass]
[DoNotParallelize]
public class HistoryMultiSelectBarTests
{
    public TestContext TestContext { get; set; } = null!;

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());

    [TestMethod]
    [DataRow("zh-CN")]
    [DataRow("en-US")]
    public async Task CommandsUseAvailableWidthAndReturnAfterResizing(string cultureName)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryMultiSelectBarTests));
        await session.Dispatch(() =>
        {
            var previousCulture = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            var status = new TextBlock
            {
                Text = string.Format(Strings.HistoryMultiSelectStatus, 2, 2),
                Margin = new Thickness(8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var bar = new HistoryMultiSelectCommandBar
            {
                Content = status,
                DefaultLabelPosition = CommandBarDefaultLabelPosition.Right
            };
            var commands = CreateCommands();
            foreach (var command in commands)
                bar.PrimaryCommands.Add(command);
            var window = new Window { Width = 1800, Height = 60, Content = bar };
            try
            {
                window.Show();
                Layout(window);
                var fittingWidth = status.DesiredSize.Width + commands.Sum(b => b.Bounds.Width) +
                    bar.Padding.Left + bar.Padding.Right + 10;
                window.Width = fittingWidth;
                Layout(window);
                Assert.HasCount(6, bar.VisiblePrimaryCommands, "All six measured buttons fit without a more button.");
                Assert.IsFalse(bar.IsOverflowButtonVisible);
                VerifyBounds(bar, status);
                window.Width = fittingWidth - 150;
                Layout(window);
                var narrowCount = bar.VisiblePrimaryCommands.Count;
                Assert.IsGreaterThan(0, narrowCount);
                Assert.IsLessThan(6, narrowCount);
                VerifyBounds(bar, status);
                VerifyOverflowAction(bar, commands[^1], window);
                window.Width = 300;
                Layout(window);
                VerifyBounds(bar, status);
                window.Width = fittingWidth - 150;
                Layout(window);
                Assert.AreEqual(narrowCount, bar.VisiblePrimaryCommands.Count);
                window.Width = fittingWidth;
                Layout(window);
                Assert.HasCount(6, bar.VisiblePrimaryCommands);
                VerifyBounds(bar, status);
            }
            finally
            {
                window.Close();
                CultureInfo.CurrentUICulture = previousCulture;
            }
            return true;
        }, TestContext.CancellationTokenSource.Token);
    }

    private static CommandBarButton[] CreateCommands() => new[]
    {
        Strings.HistoryMultiSelectAll, Strings.HistoryMultiSelectDelete, Strings.HistoryMultiSelectStar,
        Strings.HistoryTagsEdit, Strings.HistoryExportSelected, Strings.HistoryMultiSelectExit
    }.Select(label => new CommandBarButton
    {
        Label = label,
        Icon = new Border { Width = 16, Height = 16 }
    }).ToArray();

    private static void VerifyOverflowAction(CommandBar bar, CommandBarButton exit, Window window)
    {
        var invoked = 0;
        exit.Click += (_, _) => invoked++;
        var more = bar.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_OverflowButton");
        Assert.IsTrue(more.IsEffectivelyVisible);
        ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(more)!).Invoke();
        Layout(window);
        Assert.IsTrue(bar.IsOpen);
        Assert.IsTrue(exit.IsInOverflow);
        ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(exit)!).Invoke();
        Dispatcher.UIThread.RunJobs();
        Assert.AreEqual(1, invoked);
        bar.IsOpen = false;
        Layout(window);
    }

    private static void VerifyBounds(CommandBar bar, TextBlock status)
    {
        var statusEnd = status.TranslatePoint(new Point(status.Bounds.Width, 0), bar)!.Value.X;
        var more = bar.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_OverflowButton");
        Assert.AreEqual(bar.OverflowItems.Count > 0, more.IsEffectivelyVisible);
        var right = more.IsVisible ? more.TranslatePoint(default, bar)!.Value.X : bar.Bounds.Width - bar.Padding.Right;
        foreach (var command in bar.VisiblePrimaryCommands.Cast<Control>())
        {
            var start = command.TranslatePoint(default, bar)!.Value.X;
            Assert.IsGreaterThanOrEqualTo(statusEnd - 1, start);
            Assert.IsLessThanOrEqualTo(right + 1, start + command.Bounds.Width);
        }
        Assert.IsLessThanOrEqualTo(right + 1, statusEnd);
    }

    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
