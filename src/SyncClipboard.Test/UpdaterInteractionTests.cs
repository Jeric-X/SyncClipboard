using Moq;
using Sharprompt.Drivers;
using SyncClipboard.Updater;

namespace SyncClipboard.Test;

[TestClass]
public class UpdaterInteractionTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("\n", true)]
    [DataRow("y", true)]
    [DataRow("是", true)]
    [DataRow("n", false)]
    [DataRow("否", false)]
    [DataRow("", false)]
    [DataRow("invalid\nn", false)]
    public async Task ForceExitConfirmation_DefaultsToYesButDoesNotConfirmOnEndOfInput(string answer, bool expected)
    {
        using var input = new StringReader(answer);
        using var output = new StringWriter();
        var interaction = new ConsoleUpdateInteraction("zh-CN", input: input, output: output);
        Assert.AreEqual(expected, await interaction.ConfirmForceExitAsync(TestContext.CancellationTokenSource.Token));
        Assert.Contains("[Y/n]", output.ToString());
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
            Path.Combine(missing, "SyncClipboard.exe"), int.MaxValue, "en", []);
        var result = await UpdateWorker.RunAsync(update, interaction, TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(1, result);
        Assert.IsNotNull(interaction.Result);
        Assert.AreEqual(1, interaction.Result.ExitCode);
        Assert.IsNotNull(interaction.Result.Error);
    }

    private sealed class RecordingInteraction : IUpdateInteraction
    {
        public UpdateResult? Result { get; private set; }
        public void Report(string phase, int percent) { }
        public Task<bool> ConfirmForceExitAsync(CancellationToken token) => throw new AssertFailedException("Unexpected confirmation.");
        public Task<UpdateFailureAction> AskFailureActionAsync(string path, Exception error, CancellationToken token)
            => throw new AssertFailedException("Unexpected file failure prompt.");
        public Task ShowResultAsync(UpdateResult result)
        {
            Result = result;
            return Task.CompletedTask;
        }
    }
}
