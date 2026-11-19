using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SyncClipboard.Core.I18n;
using SyncClipboard.Test.WinUI3.App;
using SyncClipboard.WinUI3.Views;

namespace SyncClipboard.Test.WinUI3.History;

[TestClass]
public class HistoryTitleDialogTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("PlatformWindows")]
    public async Task InvalidTitle_PreservesDraftUntilCorrectedOrCancelled()
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
        var root = new Grid();
        var window = new Window { Content = root };
        HistoryTitleDialog? dialog = null;
        try
        {
            window.Activate();
            await WaitUntilAsync(() => root.XamlRoot is not null, timeout.Token);
            foreach (var confirm in new[] { false, true })
            {
                dialog = new HistoryTitleDialog("Edit title", "original") { XamlRoot = root.XamlRoot };
                var result = dialog.ShowAsync().AsTask();
                await WaitUntilAsync(() => Descendants<TextBox>(dialog).Any(), timeout.Token);
                var input = Descendants<TextBox>(dialog).Single();
                input.Text = new string('a', 257);
                Invoke(dialog, "PrimaryButton");
                var message = string.Format(Strings.HistoryTitleFormatError, 256);
                await WaitUntilAsync(() => Descendants<TextBlock>(dialog).Any(text => text.Text == message), timeout.Token);
                Assert.IsFalse(result.IsCompleted);
                Assert.AreEqual(new string('a', 257), input.Text);
                input.Text = " e\u0301 ";
                Invoke(dialog, confirm ? "PrimaryButton" : "CloseButton");
                Assert.AreEqual(confirm ? ContentDialogResult.Primary : ContentDialogResult.None,
                    await result.WaitAsync(timeout.Token));
                Assert.AreEqual(" e\u0301 ", dialog.Text);
            }
        }
        finally
        {
            dialog?.Hide();
            window.Content = null;
            window.Close();
        }
    }

    private static void Invoke(ContentDialog dialog, string name)
    {
        var button = Descendants<Button>(dialog).Single(b => b.Name == name);
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match)
            yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i)))
                yield return child;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken token)
    {
        while (!condition())
            await Task.Delay(20, token);
    }
}
