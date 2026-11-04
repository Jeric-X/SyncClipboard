using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.Styling;
using FluentAvalonia.UI.Controls;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Desktop.Views;

namespace SyncClipboard.Test.Desktop.Client.History;

[TestClass]
[DoNotParallelize]
public class HistoryTagEditorDialogTests
{
    private static readonly string[] AddedTags = ["工作"];

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia();

    private static void ClickControl(Control control)
    {
        var window = (Window)TopLevel.GetTopLevel(control)!;
        window.UpdateLayout();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [TestMethod]
    public async Task Editor_SearchesCreatesAndPreservesMixedSelectionUntilCommitted()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryTagEditorDialogTests));
        await session.Dispatch(async () =>
        {
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            var vm = new HistoryTagEditorViewModel(["代码"], [["Prompt", "工作"], ["Prompt"]]);
            var dialog = new HistoryTagEditorDialog(vm);
            var owner = new Window { Width = 850, Height = 650, Content = new Button() };
            try
            {
                owner.Show();
                var result = dialog.ShowAsync(owner);
                Dispatcher.UIThread.RunJobs();
                var checks = dialog.GetVisualDescendants().OfType<CheckBox>().ToArray();
                Assert.HasCount(3, checks);
                Assert.IsNull(checks.Single(c => Equals(c.Content, "工作")).IsChecked);
                ClickControl(checks.Single(c => Equals(c.Content, "工作")));
                Dispatcher.UIThread.RunJobs();
                CollectionAssert.AreEqual(AddedTags, vm.Added);
                var search = dialog.GetVisualDescendants().OfType<TextBox>().Single();
                search.Text = "新标签";
                Dispatcher.UIThread.RunJobs();
                Assert.IsEmpty(dialog.GetVisualDescendants().OfType<CheckBox>());
                ClickControl(dialog.GetVisualDescendants().OfType<Button>()
                    .Single(b => Equals(b.Content, Strings.HistoryTagsAdd)));
                Dispatcher.UIThread.RunJobs();
                Assert.Contains("新标签", vm.Added);
                Assert.IsEmpty(vm.Removed);
                dialog.Hide();
                Assert.AreEqual(FAContentDialogResult.None, await result);
            }
            finally
            {
                dialog.Hide();
                owner.Close();
            }
            return true;
        }, CancellationToken.None);
    }
}
