using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Test.WinUI3.App;
using SyncClipboard.WinUI3.Views;
using Windows.Graphics;

namespace SyncClipboard.Test.WinUI3.Clipboard;

[TestClass]
public class TextFilterRuleEditDialogTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("PlatformWindows")]
    public async Task RegexMustBeValidBeforeConfirmingRule()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("WinUI needs Windows.");
            return;
        }
        await UITestProcess.RunAsync(EditRegexAsync, TestContext.CancellationTokenSource.Token);
    }

    private static async Task EditRegexAsync()
    {
        var root = new Grid();
        var window = new Window { Content = root };
        window.AppWindow.Move(new PointInt32(-32000, -32000));
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        root.Loaded += (_, _) => loaded.TrySetResult();
        try
        {
            window.Activate();
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var editor = new EditableTextFilterRule(new TextFilterRule("["), canExcludeFromHistory: true);
            var dialog = new TextFilterRuleEditDialog(editor) { XamlRoot = root.XamlRoot };
            var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.Opened += (_, _) => opened.TrySetResult();
            var result = dialog.ShowAsync();
            try
            {
                await opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
                var fields = ((StackPanel)dialog.Content).Children;
                Assert.IsTrue(dialog.IsPrimaryButtonEnabled);
                fields.OfType<ComboBox>().Single().SelectedItem = EditableTextFilterRule.MatchModes.Single(mode => mode.Key);
                Assert.IsFalse(dialog.IsPrimaryButtonEnabled);
                fields.OfType<TextBox>().Single().Text = "[0-9]+";
                Assert.IsTrue(dialog.IsPrimaryButtonEnabled);
                fields.OfType<CheckBox>().Single().IsChecked = true;
                Assert.AreEqual(new TextFilterRule("[0-9]+", UseRegex: true, ExcludeFromHistory: true), editor.ToRule());
            }
            finally
            {
                dialog.Hide();
                await result;
            }
        }
        finally
        {
            window.Close();
        }
    }
}
