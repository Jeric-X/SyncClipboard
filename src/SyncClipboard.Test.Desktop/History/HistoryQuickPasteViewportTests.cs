using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using SyncClipboard.Core.ViewModels.Sub;
using SyncClipboard.Desktop.Views;
using Fixture = SyncClipboard.Test.Client.History.HistoryViewModelPagingTests.Fixture;

namespace SyncClipboard.Test.Desktop.History;

[TestClass]
[DoNotParallelize]
public class HistoryQuickPasteViewportTests
{
    public TestContext TestContext { get; set; } = null!;

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions());

    [TestMethod]
    public async Task NumberingStartsWithTheFirstPartiallyVisibleRowAfterScrolling()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await Fixture.CreateAsync(token);
        await fixture.LoadAsync();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryQuickPasteViewportTests));
        await session.Dispatch(() =>
        {
            Application.Current!.Styles.Add(new FluentTheme());
            var records = fixture.Records.ToArray();
            var list = new ListBox
            {
                ItemsSource = records,
                Height = 200,
                ItemTemplate = new FuncDataTemplate<HistoryRecordVM>((record, _) => new TextBlock { Text = record?.Text, Height = 40 })
            };
            var window = new Window { Content = list, Width = 300, Height = 200 };
            try
            {
                window.Show();
                window.UpdateLayout();
                Assert.AreSame(records[0], HistoryWindow.GetVisibleHistoryRecords(list)[0]);
                var rowHeight = list.ContainerFromIndex(0)!.Bounds.Height;
                var scroll = (ScrollViewer)list.Scroll!;
                scroll.Offset = new Vector(0, rowHeight * 7.5);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var visible = HistoryWindow.GetVisibleHistoryRecords(list);
                var count = (int)Math.Ceiling((scroll.Viewport.Height + (rowHeight / 2)) / rowHeight);
                CollectionAssert.AreEqual(records.Skip(7).Take(count).ToArray(), visible.ToArray());
            }
            finally
            {
                window.Close();
            }
            return true;
        }, token);
    }
}
