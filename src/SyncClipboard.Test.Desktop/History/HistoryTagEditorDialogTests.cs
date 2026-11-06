using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
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

namespace SyncClipboard.Test.Desktop.History;

[TestClass]
[DoNotParallelize]
public class HistoryTagEditorDialogTests
{
    private static readonly string[] AddedTags = ["工作"];

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia();

    private static void ClickControl(Control control)
    {
        var window = TopLevel.GetTopLevel(control)!;
        window.UpdateLayout();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OpeningEditor_FocusesTagSearchForTyping(bool fromContextMenu)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryTagEditorDialogTests));
        await session.Dispatch(async () =>
        {
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            var historySearch = new TextBox { Text = "history query" };
            var anchor = new Button { Content = "record" };
            var owner = new Window
            {
                Width = 850,
                Height = 650,
                Content = new StackPanel { Children = { historySearch, anchor } }
            };
            var editor = new HistoryTagEditorViewModel(["code"], [["code"]]);
            var dialog = new HistoryTagEditorDialog(editor);
            var menu = new FAMenuFlyout();
            var edit = new FAMenuFlyoutItem { Text = "Edit tags" };
            menu.Items.Add(edit);
            Task<FAContentDialogResult>? result = null;
            edit.Click += (_, _) => result = dialog.ShowAsync(owner);
            try
            {
                owner.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.IsTrue(historySearch.Focus());
                if (fromContextMenu)
                {
                    menu.ShowAt(anchor);
                    Dispatcher.UIThread.RunJobs();
                    ClickControl(edit);
                }
                else
                {
                    result = dialog.ShowAsync(owner);
                }
                Dispatcher.UIThread.RunJobs();
                var search = dialog.GetVisualDescendants().OfType<TextBox>().Single();
                Assert.IsTrue(search.IsFocused, $"Focused element: {owner.FocusManager?.GetFocusedElement()}");
                owner.KeyTextInput("code");
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual("code", editor.Search);
                Assert.AreEqual("history query", historySearch.Text);
                Assert.IsNotNull(result);
                dialog.Hide();
                await result;
            }
            finally
            {
                menu.Hide();
                dialog.Hide();
                owner.Close();
            }
            return true;
        }, CancellationToken.None);
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
                ((IToggleProvider)ControlAutomationPeer.CreatePeerForElement(checks.Single(c => Equals(c.Content, "工作")))!).Toggle();
                Dispatcher.UIThread.RunJobs();
                CollectionAssert.AreEqual(AddedTags, vm.Added);
                var search = dialog.GetVisualDescendants().OfType<TextBox>().Single();
                search.Text = "新标签";
                Dispatcher.UIThread.RunJobs();
                Assert.IsEmpty(dialog.GetVisualDescendants().OfType<CheckBox>());
                var add = dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, Strings.HistoryTagsAdd));
                ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(add)!).Invoke();
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
