using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.Styling;
using SyncClipboard.Core.ViewModels.Sub;
using SyncClipboard.Desktop.Controls;
using SyncClipboard.Test.Client.History;

namespace SyncClipboard.Test.Desktop.Client.History;

[TestClass]
[DoNotParallelize]
public class PreviewPanelTagTests
{
    public TestContext TestContext { get; set; } = null!;

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());

    [TestMethod]
    public async Task PreviewShowsCurrentTagsAndRemovesOnlyTheClickedRecordsTag()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var session = HeadlessUnitTestSession.StartNew(typeof(PreviewPanelTagTests));
        await session.Dispatch(async () =>
        {
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            await using var fixture = await HistoryViewModelPagingTests.Fixture.CreateAsync(token);
            await fixture.AddTagsAsync();
            await fixture.LoadAsync();
            var matches = fixture.Records.Where(record => record.Tags is ["Red", "Blue"]).Take(2).ToArray();
            var first = matches[0];
            var second = matches[1];
            var originalTags = first.Tags.ToArray();
            fixture.ViewModel.EnterMultiSelect(first);
            fixture.ViewModel.EnterMultiSelect(second);
            var panel = new PreviewPanel { ViewModel = fixture.ViewModel, SelectedItem = first };
            var search = new TextBox();
            var window = new Window
            {
                Content = new StackPanel { Children = { search, panel } },
                Width = 550,
                Height = 350
            };
            var tags = panel.FindControl<ItemsControl>("_PreviewTags")!;

            void UpdateLayout()
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            }
            Point Center(Button button) =>
                button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            string?[] Names()
            {
                UpdateLayout();
                return tags.GetVisualDescendants().OfType<TextBlock>()
                    .Where(label => label.FindAncestorOfType<Button>() is null).Select(label => label.Text).ToArray();
            }
            Button RemoveButton(string name) => tags.GetVisualDescendants().OfType<Button>()
                .Single(button => button.DataContext is HistoryPreviewTag tag && tag.Name == name);

            try
            {
                window.Show();
                search.Focus();
                CollectionAssert.AreEqual(first.Tags, Names());

                var pressed = Center(RemoveButton("Red"));
                window.MouseDown(pressed, MouseButton.Left);
                panel.SelectedItem = second;
                CollectionAssert.AreEqual(second.Tags, Names());
                window.MouseUp(pressed, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                CollectionAssert.AreEqual(originalTags, (await fixture.ReadStoredRecordAsync(first)).Tags);
                CollectionAssert.AreEqual(second.Tags, (await fixture.ReadStoredRecordAsync(second)).Tags);

                var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                fixture.Manager.HistoryUpdated += record => Dispatcher.UIThread.Post(() =>
                {
                    fixture.Publish(new HistoryRecordVM(record));
                    saved.TrySetResult();
                });
                var click = Center(RemoveButton("Blue"));
                window.MouseDown(click, MouseButton.Left);
                window.MouseUp(click, MouseButton.Left);
                await saved.Task.WaitAsync(TimeSpan.FromSeconds(5), token);

                string[] remaining = ["Red"];
                CollectionAssert.AreEqual(remaining, (await fixture.ReadStoredRecordAsync(second)).Tags);
                CollectionAssert.AreEqual(originalTags, (await fixture.ReadStoredRecordAsync(first)).Tags);
                CollectionAssert.AreEqual(remaining, Names());
                Assert.AreSame(search, window.FocusManager!.GetFocusedElement());
                panel.SelectedItem = first;
                CollectionAssert.AreEqual(first.Tags, Names());
                panel.SelectedItem = null!;
                Assert.IsEmpty(Names());
            }
            finally
            {
                window.Close();
            }
            return true;
        }, token);
    }
}
