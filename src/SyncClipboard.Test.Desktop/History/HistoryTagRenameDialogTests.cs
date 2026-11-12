using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
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
public class HistoryTagRenameDialogTests
{
    public TestContext TestContext { get; set; } = null!;

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DuplicateName_LeavesDialogOpenUntilConfirmOrCancel(bool confirm)
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryTagRenameDialogTests));
        await session.Dispatch(async () =>
        {
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            var writes = 0;
            var model = new HistoryTagRenameViewModel("old", (_, merge) =>
            {
                if (merge)
                    writes++;
                return Task.FromResult(merge);
            });
            var dialog = new HistoryTagRenameDialog(model);
            var owner = new Window { Width = 450, Height = 550 };
            try
            {
                owner.Show();
                var result = dialog.ShowAsync(owner);
                await SettleAsync(token);
                var input = dialog.GetVisualDescendants().OfType<TextBox>().Single();
                Assert.AreEqual("old", input.SelectedText);
                input.Text = "existing";
                await SettleAsync(token);
                Invoke(dialog, "PrimaryButton");
                await SettleAsync(token);
                Assert.IsFalse(result.IsCompleted);
                Assert.AreEqual(0, writes);
                Assert.IsTrue(dialog.GetVisualDescendants().OfType<TextBlock>()
                    .Any(text => text.Text == string.Format(Strings.HistoryTagMergeConfirmation, "existing")));
                Assert.AreEqual(Strings.Confirm, dialog.PrimaryButtonText);
                Assert.AreEqual(Strings.Cancel, dialog.CloseButtonText);
                Invoke(dialog, confirm ? "PrimaryButton" : "CloseButton");
                Assert.AreEqual(confirm ? FAContentDialogResult.Primary : FAContentDialogResult.None, await result);
                Assert.AreEqual(confirm ? 1 : 0, writes);
            }
            finally
            {
                owner.Close();
            }
        }, token);
    }

    private static void Invoke(HistoryTagRenameDialog dialog, string name)
    {
        var button = dialog.GetVisualDescendants().OfType<Button>().Single(control => control.Name == name);
        ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(button)!).Invoke();
    }

    private static async Task SettleAsync(CancellationToken token)
    {
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(300, token);
        Dispatcher.UIThread.RunJobs();
    }
}
