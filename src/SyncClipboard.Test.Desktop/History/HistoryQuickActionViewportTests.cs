using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Styling;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using SyncClipboard.Core.ViewModels.Sub;
using SyncClipboard.Desktop.Views;
using Fixture = SyncClipboard.Test.Client.History.HistoryViewModelPagingTests.Fixture;

namespace SyncClipboard.Test.Desktop.History;

[TestClass]
[DoNotParallelize]
public class HistoryQuickActionViewportTests
{
    public TestContext TestContext { get; set; } = null!;

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions());

    [TestMethod]
    [DataRow(20, 7, 10)]
    [DataRow(21, 8, 10)]
    [DataRow(30, 8, 11)]
    public async Task VisibleRowsIncludeAtLeastSixtyPercentOfTheirHeight(int clippedPixels, int first, int last)
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await Fixture.CreateAsync(token);
        await fixture.LoadAsync();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryQuickActionViewportTests));
        await session.Dispatch(() =>
        {
            Application.Current!.Styles.Add(new FluentTheme());
            var records = fixture.Records.ToArray();
            var list = new ListBox
            {
                ItemsSource = records,
                Height = 200,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                ItemTemplate = new FuncDataTemplate<HistoryRecordVM>((record, _) => new TextBlock { Text = record?.Text, Height = 40 })
            };
            list.Styles.Add(new Style(selector => selector.OfType<ListBoxItem>())
            {
                Setters = { new Setter(ListBoxItem.HeightProperty, 50d) }
            });
            var window = new Window { Content = list, Width = 300, Height = 200 };
            try
            {
                window.Show();
                window.UpdateLayout();
                Assert.AreSame(records[0], HistoryWindow.GetVisibleHistoryRecords(list)[0]);
                var scroll = (ScrollViewer)list.Scroll!;
                scroll.Offset = new Vector(0, (7 * 50) + clippedPixels);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var visible = HistoryWindow.GetVisibleHistoryRecords(list);
                CollectionAssert.AreEqual(records[first..(last + 1)], visible.ToArray());
            }
            finally
            {
                window.Close();
            }
            return true;
        }, token);
    }
}
