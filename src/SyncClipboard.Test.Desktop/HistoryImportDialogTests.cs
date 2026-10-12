using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.Styling;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Desktop.Views;

namespace SyncClipboard.Test.Desktop;

[TestClass]
[DoNotParallelize]
public class HistoryImportDialogTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions());

    [TestMethod]
    public async Task DialogShowsStagesAndWaitsForCancellationBeforeClosing()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryImportDialogTests));
        await session.Dispatch(async () =>
        {
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            using var viewModel = new HistoryImportViewModel(null!);
            var dialog = new HistoryImportDialog(viewModel, () =>
            {
                viewModel.BackupPath = "/backup.zip";
                viewModel.Ready = true;
                viewModel.Status = "Ready";
                return Task.CompletedTask;
            });
            var window = new Window { Width = 900, Height = 800, Content = new Button() };
            try
            {
                window.Show();
                var result = dialog.ShowAsync(window);
                Dispatcher.UIThread.RunJobs();
                var start = dialog.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, Strings.HistoryImportStart));
                Assert.IsTrue(start.IsVisible && start.IsEffectivelyEnabled);
                viewModel.IsBusy = true;
                Dispatcher.UIThread.RunJobs();
                Assert.IsFalse(start.IsVisible);
                dialog.Hide();
                Dispatcher.UIThread.RunJobs();
                Assert.IsFalse(result.IsCompleted);
                viewModel.IsFinished = true;
                viewModel.IsBusy = false;
                viewModel.ReportPath = "/not-imported.txt";
                Dispatcher.UIThread.RunJobs();
                Assert.IsFalse(start.IsVisible);
                Assert.IsTrue(dialog.GetVisualDescendants().OfType<SelectableTextBlock>().Any(block => block.Text == viewModel.ReportPath));
                dialog.Hide();
                await result;
            }
            finally
            {
                viewModel.IsBusy = false;
                dialog.Hide();
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }
}
