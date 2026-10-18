using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.Styling;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Desktop.Utilities;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.Utilities.History;
using System.Runtime.CompilerServices;
using SyncClipboard.Desktop.Views;

namespace SyncClipboard.Test.Desktop;

[TestClass]
[DoNotParallelize]
public class HistoryExportDialogTests
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions());

    [TestMethod]
    public async Task DialogShowsCurrentStageAndWaitsForCancellationToFinish()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryExportDialogTests));
        await session.Dispatch(async () =>
        {
            Application.Current!.Styles.Add(new FluentAvaloniaTheme());
            var manager = (HistoryManager)RuntimeHelpers.GetUninitializedObject(typeof(HistoryManager));
            using var backupSession = manager.TryBeginBackupSession();
            Assert.IsNotNull(backupSession);
            using var viewModel = new HistoryExportViewModel(null!, null!);
            var initialized = false;
            var dialog = new HistoryExportDialog(viewModel, () =>
            {
                initialized = true;
                viewModel.Ready = true;
                viewModel.Directory = "/selected/directory";
                viewModel.Status = "Ready";
                return Task.CompletedTask;
            });
            var window = new Window { Width = 900, Height = 800, Content = new Button { Content = "Background" } };
            try
            {
                window.Show();
                var result = dialog.ShowAsync(window);
                Dispatcher.UIThread.RunJobs();
                await AssertOtherOperationBlockedAsync(manager, import: true);
                Assert.IsTrue(initialized);
                var start = dialog.GetVisualDescendants().OfType<Button>()
                    .Single(button => Equals(button.Content, Strings.HistoryExportStart));
                var changeDirectory = dialog.GetVisualDescendants().OfType<Button>()
                    .Single(button => Equals(button.Content, Strings.HistoryExportChangeDirectory));
                var openFolder = dialog.GetVisualDescendants().OfType<Button>()
                    .Single(button => Equals(button.Content, Strings.OpenFolder));
                var directory = dialog.GetVisualDescendants().OfType<TextBox>().Single();
                var progress = dialog.GetVisualDescendants().OfType<ProgressBar>().Single();
                Assert.IsTrue(start.IsVisible && start.IsEffectivelyEnabled);
                Assert.IsTrue(changeDirectory.IsVisible && directory.IsVisible);
                Assert.IsFalse(progress.IsVisible || openFolder.IsVisible);
                viewModel.Ready = false;
                viewModel.IsBusy = true;
                viewModel.IsPreparing = true;
                viewModel.Status = "Exporting";
                Dispatcher.UIThread.RunJobs();
                Assert.IsFalse(start.IsVisible || changeDirectory.IsVisible || directory.IsVisible);
                Assert.IsTrue(progress.IsVisible);
                Assert.IsTrue(progress.IsIndeterminate);
                viewModel.IsPreparing = false;
                viewModel.TotalCount = 10;
                viewModel.ProcessedCount = 4;
                Dispatcher.UIThread.RunJobs();
                Assert.IsFalse(progress.IsIndeterminate);
                Assert.AreEqual(10d, progress.Maximum);
                Assert.AreEqual(4d, progress.Value);
                Assert.AreEqual(Strings.Cancel, dialog.CloseButtonText);
                dialog.Hide();
                Dispatcher.UIThread.RunJobs();
                Assert.IsFalse(result.IsCompleted, "Closing must wait for the export to stop and display its result.");
                viewModel.IsFinished = true;
                viewModel.IsBusy = false;
                viewModel.Status = "Export canceled; report saved";
                viewModel.ReportPath = "/selected/directory/report.txt";
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(Strings.HistoryExportClose, dialog.CloseButtonText);
                Assert.IsTrue(openFolder.IsVisible && openFolder.IsEffectivelyEnabled);
                Assert.IsFalse(start.IsVisible || changeDirectory.IsVisible || directory.IsVisible || progress.IsVisible);
                Assert.IsTrue(dialog.GetVisualDescendants().OfType<SelectableTextBlock>()
                    .Any(block => block.Text == viewModel.ReportPath));
                dialog.Hide();
                await result;
                backupSession.Dispose();
                using var nextSession = manager.TryBeginBackupSession();
                Assert.IsNotNull(nextSession);
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

    internal static async Task AssertOtherOperationBlockedAsync(HistoryManager manager, bool import)
    {
        var window = new TestMainWindow();
        using var services = new ServiceCollection().AddSingleton((IMainWindow)window).BuildServiceProvider();
        try
        {
            window.Show();
            var showing = import
                ? new HistoryImportDialogPresenter(services, manager).ShowAsync()
                : new HistoryExportDialogPresenter(services, manager).ShowAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.IsFalse(showing.IsCompleted);
            var message = window.GetVisualDescendants().OfType<FAContentDialog>().Single();
            Assert.AreEqual(import ? Strings.HistoryImportBusy : Strings.HistoryExportBusy,
                ((TextBlock)message.Content!).Text);
            message.Hide();
            await showing;
            using var unexpectedSession = manager.TryBeginBackupSession();
            Assert.IsNull(unexpectedSession, "A rejected operation must not release the active operation.");
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class TestMainWindow : Window, IMainWindow
    {
        public void NavigateTo(PageDefinition page, NavigationTransitionEffect effect, object? para) => throw new NotSupportedException();
        public void OpenPage(PageDefinition page, object? para = null) => throw new NotSupportedException();
        public void NavigateToLastLevel() => throw new NotSupportedException();
        public void NavigateToNextLevel(PageDefinition page, object? para) => throw new NotSupportedException();
    }
}
