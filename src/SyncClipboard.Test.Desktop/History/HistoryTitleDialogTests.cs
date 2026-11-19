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
using SyncClipboard.Desktop.Views;

namespace SyncClipboard.Test.Desktop.History;

[TestClass]
[DoNotParallelize]
public class HistoryTitleDialogTests
{
    public TestContext TestContext { get; set; } = null!;

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InvalidTitle_PreservesDraftUntilCorrectedOrCancelled(bool confirm)
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryTitleDialogTests));
        await session.Dispatch(async () =>
        {
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            var dialog = new HistoryTitleDialog("Edit title", "original");
            var owner = new Window { Width = 450, Height = 550 };
            try
            {
                owner.Show();
                var result = dialog.ShowAsync(owner);
                await SettleAsync(token);
                var input = dialog.GetVisualDescendants().OfType<TextBox>().Single();
                Assert.AreEqual("original", input.SelectedText);
                input.Text = new string('a', 257);
                await SettleAsync(token);
                Invoke(dialog, "PrimaryButton");
                await SettleAsync(token);
                Assert.IsFalse(result.IsCompleted);
                Assert.AreEqual(new string('a', 257), input.Text);
                Assert.IsTrue(dialog.GetVisualDescendants().OfType<TextBlock>()
                    .Any(text => text.Text == string.Format(Strings.HistoryTitleFormatError, 256)));
                Assert.AreEqual(Strings.Confirm, dialog.PrimaryButtonText);
                Assert.AreEqual(Strings.Cancel, dialog.CloseButtonText);
                input.Text = " e\u0301 ";
                Invoke(dialog, confirm ? "PrimaryButton" : "CloseButton");
                Assert.AreEqual(confirm ? FAContentDialogResult.Primary : FAContentDialogResult.None, await result);
                Assert.AreEqual(" e\u0301 ", dialog.Text);
            }
            finally
            {
                owner.Close();
            }
        }, token);
    }

    private static void Invoke(HistoryTitleDialog dialog, string name)
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
