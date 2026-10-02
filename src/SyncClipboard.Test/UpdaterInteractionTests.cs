using SyncClipboard.Updater;

namespace SyncClipboard.Test;

[TestClass]
public class UpdaterInteractionTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task Worker_ReportsPreparationFailureThroughInteraction()
    {
        var interaction = new RecordingInteraction();
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var update = new UpdateArguments("missing.zip", "sha256:" + new string('A', 64), missing,
            int.MaxValue, "en", []);
        var result = await UpdateWorker.RunAsync(update, interaction, TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(1, result);
        Assert.IsNotNull(interaction.Result);
        Assert.AreEqual(1, interaction.Result.ExitCode);
        Assert.IsNotNull(interaction.Result.Error);
    }

    [TestMethod]
    public async Task StartupFailure_DisplaysCauseAndWaitsForAcknowledgement()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            Assert.Inconclusive("Requires an updater-supported platform.");
        var shown = new TaskCompletionSource<UpdateResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var acknowledged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var interaction = new RecordingInteraction
        {
            OnResult = result =>
            {
                shown.TrySetResult(result);
                return acknowledged.Task;
            }
        };
        var run = UpdateRunner.RunAsync(["--digest", "invalid", "--language", "zh-CN"],
            interaction, TestContext.CancellationTokenSource.Token);
        try
        {
            var result = await shown.Task.WaitAsync(TestContext.CancellationTokenSource.Token);
            Assert.AreEqual(1, result.ExitCode);
            Assert.AreEqual(UpdaterText.ForLanguage("zh-CN").InvalidDigest, result.Error);
            Assert.IsFalse(run.IsCompleted);
        }
        finally
        {
            acknowledged.TrySetResult();
        }
        Assert.AreEqual(1, await run);
    }

    [TestMethod]
    public async Task DirectoryPreflight_RetriesMissingDirectoryWithoutRequestingElevation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "updater-probe-" + Guid.NewGuid().ToString("N"));
        var prompts = 0;
        var interaction = new RecordingInteraction
        {
            OnFailure = (path, error, canRollback, _) =>
            {
                prompts++;
                Assert.Contains(directory, path);
                Assert.IsFalse(canRollback);
                Assert.IsInstanceOfType<DirectoryNotFoundException>(error);
                Directory.CreateDirectory(directory);
                return Task.FromResult(UpdateFailureAction.Retry);
            }
        };
        try
        {
            Assert.IsFalse(await UpdateWorker.RequiresElevationAsync([directory], false, interaction,
                TestContext.CancellationTokenSource.Token));
            Assert.IsEmpty(Directory.GetFileSystemEntries(directory));
            Assert.AreEqual(1, prompts);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    private sealed class RecordingInteraction : IUpdateInteraction
    {
        public UpdateResult? Result { get; private set; }
        public Func<UpdateResult, Task>? OnResult { get; init; }
        public UpdateFailureHandler? OnFailure { get; init; }
        public void Report(string phase, int percent) { }
        public Task<ForceExitAction> ConfirmForceExitAsync(CancellationToken token) => throw new AssertFailedException("Unexpected confirmation.");
        public Task<UpdateFailureAction> AskFailureActionAsync(string path, Exception error, bool canRollback, CancellationToken token)
            => OnFailure?.Invoke(path, error, canRollback, token) ?? throw new AssertFailedException("Unexpected file failure prompt.");
        public Task ShowResultAsync(UpdateResult result)
        {
            Result = result;
            return OnResult?.Invoke(result) ?? Task.CompletedTask;
        }
    }
}
