using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Utilities.Updater;
using SyncClipboard.Desktop.Utilities.Updater;

namespace SyncClipboard.Test.Desktop;

[TestClass]
[DoNotParallelize]
public class UpdateHelperWindowTests
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions());

    [TestMethod]
    public async Task InvalidPayload_ShowsFailureAndLogButtonWithoutLoadingNormalApplication()
    {
        var work = Directory.CreateTempSubdirectory("Updater window 中文 ").FullName;
        var stage = Path.Combine(work, "payload");
        File.WriteAllText(stage, "corrupt");
        var task = new UpdateInstallTask
        {
            Directory = work,
            Kind = "AppImage",
            Stage = stage,
            Target = "unused",
            Backup = "unused",
            Executable = "unused",
            Version = "v9.0.0",
            ProcessId = int.MaxValue,
            Digest = "sha256:wrong"
        };
        try
        {
            await using var session = HeadlessUnitTestSession.StartNew(typeof(UpdateHelperWindowTests));
            await session.Dispatch(async () =>
            {
                Application.Current!.Styles.Add(new FluentTheme());
                var window = new UpdateHelperApplication(task).CreateWindow();
                window.Show();
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    var button = window.GetVisualDescendants().OfType<Button>().Single();
                    while (!button.IsVisible)
                    {
                        Dispatcher.UIThread.RunJobs();
                        await Task.Delay(10, timeout.Token);
                    }
                    Assert.AreEqual(Strings.OpenUpdateLogs, button.Content);
                    Assert.IsTrue(window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == Strings.UpdateInstallFailed));
                    Assert.IsFalse(window.GetVisualDescendants().OfType<ProgressBar>().Single().IsIndeterminate);
                    Assert.IsFalse(File.Exists(Path.Combine(work, "ready")));
                }
                finally { window.Close(); }
            }, CancellationToken.None);
        }
        finally { Directory.Delete(work, true); }
    }
}
