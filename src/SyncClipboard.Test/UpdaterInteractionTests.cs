using Moq;
using Sharprompt.Drivers;
using SyncClipboard.Updater;

namespace SyncClipboard.Test;

[TestClass]
public class UpdaterInteractionTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("\n", "Yes")]
    [DataRow("y", "Yes")]
    [DataRow("是", "Yes")]
    [DataRow("n", "No")]
    [DataRow("否", "No")]
    [DataRow("", "No")]
    [DataRow("invalid\nn", "No")]
    [DataRow("r", "Retry")]
    [DataRow("重试", "Retry")]
    public async Task ForceExitConfirmation_DefaultsToYesButDoesNotConfirmOnEndOfInput(string answer, string expected)
    {
        using var input = new StringReader(answer);
        using var output = new StringWriter();
        var interaction = new ConsoleUpdateInteraction("zh-CN", input: input, output: output);
        Assert.AreEqual(Enum.Parse<ForceExitAction>(expected), await interaction.ConfirmForceExitAsync(TestContext.CancellationTokenSource.Token));
        Assert.Contains("[Y/n/r]", output.ToString());
    }

    [TestMethod]
    public void PromptDriver_IgnoresEscapeAndPreservesOtherKeys()
    {
        var inner = new Mock<IConsoleDriver>();
        inner.SetupSequence(driver => driver.ReadKey())
            .Returns(new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false))
            .Returns(new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false))
            .Returns(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        using var driver = new UpdaterConsoleDriver(inner.Object);
        Assert.AreEqual(ConsoleKey.Enter, driver.ReadKey().Key);
        inner.Verify(driver => driver.ReadKey(), Times.Exactly(3));
    }

    [TestMethod]
    public async Task RecoveryFailure_ShowsExactBackupAndWorkspacePaths()
    {
        using var input = new StringReader("");
        using var output = new StringWriter();
        var interaction = new ConsoleUpdateInteraction("zh-CN", input: input, output: output);
        await interaction.ShowResultAsync(new UpdateResult(2, "回滚失败", "workspace", "workspace/attempt/backup"));
        Assert.Contains("回滚失败", output.ToString());
        Assert.Contains("备份目录: workspace/attempt/backup", output.ToString());
        Assert.Contains("日志及工作目录: workspace", output.ToString());
    }

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
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
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
        var run = SyncClipboard.Updater.Program.RunUpdateAsync(["--digest", "invalid", "--language", "zh-CN"],
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
    public async Task FailureOutsideReplacement_ShowsCauseAndDoesNotOfferRollback()
    {
        using var input = new StringReader("3\n2\n");
        using var output = new StringWriter();
        var interaction = new ConsoleUpdateInteraction("zh-CN", input: input, output: output);
        var choice = await interaction.AskFailureActionAsync("复制: package.zip", new UnauthorizedAccessException("Access denied"),
            false, TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(UpdateFailureAction.Retry, choice);
        Assert.Contains("package.zip", output.ToString());
        Assert.Contains("Access denied", output.ToString());
        Assert.DoesNotContain("回滚", output.ToString());
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
