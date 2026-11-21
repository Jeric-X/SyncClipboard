using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Shared.Models;
using SyncClipboard.Test.WinUI3.App;
using SyncClipboard.WinUI3.Views;
using Windows.Graphics;

namespace SyncClipboard.Test.WinUI3.Clipboard;

[TestClass]
public class FilterRuleEditDialogTests
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
            var textEditor = new EditableTextFilterRule(new TextFilterRule("["), canExcludeFromHistory: true);
            await EditRegexInDialogAsync(root, textEditor, Strings.KeywordMatch);
            Assert.AreEqual(new TextFilterRule("[0-9]+", UseRegex: true, ExcludeFromHistory: true), textEditor.ToRule());

            var fileEditor = new FileFilterRuleEditor { Pattern = "[" };
            await EditRegexInDialogAsync(root, fileEditor, Strings.SuffixMatch);
            Assert.AreEqual(FileFilterMatchMode.Regex, fileEditor.ToRule().MatchMode);
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task EditRegexInDialogAsync(Grid root, FilterRuleEditor editor, string expectedLiteralMode)
    {
        var dialog = new FilterRuleEditDialog(editor) { XamlRoot = root.XamlRoot };
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.Opened += (_, _) => opened.TrySetResult();
        var result = dialog.ShowAsync();
        try
        {
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var fields = ((StackPanel)dialog.Content).Children;
            var mode = fields.OfType<ComboBox>().Single();
            Assert.AreEqual(expectedLiteralMode, ((LocaleString<bool>)mode.SelectedItem).ShownString);
            Assert.IsTrue(dialog.IsPrimaryButtonEnabled);
            mode.SelectedItem = editor.MatchModes.Single(option => option.Key);
            Assert.IsFalse(dialog.IsPrimaryButtonEnabled);
            fields.OfType<TextBox>().Single().Text = "[0-9]+";
            Assert.IsTrue(dialog.IsPrimaryButtonEnabled);
            var history = fields.OfType<CheckBox>().Single();
            Assert.AreEqual(editor.CanExcludeFromHistory ? Visibility.Visible : Visibility.Collapsed, history.Visibility);
            if (history.Visibility == Visibility.Visible)
                history.IsChecked = true;
        }
        finally
        {
            dialog.Hide();
            await result;
        }
    }
}
