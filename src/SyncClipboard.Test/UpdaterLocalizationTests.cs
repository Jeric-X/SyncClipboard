using SyncClipboard.Updater;

namespace SyncClipboard.Test;

[TestClass]
public class UpdaterLocalizationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("zh-CN", true)]
    [DataRow("ZH-tw", true)]
    [DataRow("en-US", false)]
    [DataRow("fr", false)]
    [DataRow("", false)]
    public async Task Console_UsesSelectedLanguageAndFallsBackToEnglish(string language, bool chinese)
    {
        using var input = new StringReader("n\n");
        using var output = new StringWriter();
        var interaction = new ConsoleUpdateInteraction(language, input: input, output: output);

        interaction.Report("installing", 50);
        await interaction.ConfirmForceExitAsync(TestContext.CancellationTokenSource.Token);
        await interaction.ShowResultAsync(new UpdateResult(0));

        var result = output.ToString();
        if (chinese)
        {
            Assert.Contains("安装更新: 50%", result);
            Assert.Contains("是否强制退出并继续更新？", result);
            Assert.Contains("更新完成。", result);
            Assert.DoesNotContain("Update completed.", result);
            Assert.DoesNotContain("Force it to exit", result);
        }
        else
        {
            Assert.Contains("Install the update: 50%", result);
            Assert.Contains("Force it to exit", result);
            Assert.Contains("Update completed.", result);
            Assert.DoesNotContain("更新", result);
        }
    }

    [TestMethod]
    public void ArgumentErrors_UseLanguageEvenWhenParsingFails()
    {
        var previous = UpdaterText.Current;
        try
        {
            string[] args = ["--unknown", "--language", "zh-CN"];
            UpdaterText.Current = UpdaterText.FromArguments(args);
            var error = Assert.Throws<ArgumentException>(() => UpdateArguments.Parse(args));
            Assert.AreEqual("未知或不完整的更新器参数: --unknown", error.Message);

            UpdaterText.Current = UpdaterText.FromArguments(["--language"]);
            error = Assert.Throws<ArgumentException>(() => UpdateArguments.Parse(["--language"]));
            Assert.AreEqual("Unknown or incomplete updater argument: --language", error.Message);
        }
        finally
        {
            UpdaterText.Current = previous;
        }
    }

    [TestMethod]
    public async Task Workers_KeepLanguageLocalToEachAsyncExecution()
    {
        var previous = UpdaterText.Current;
        await Task.WhenAll(CheckWorkerLanguageAsync("zh-CN"), CheckWorkerLanguageAsync("en"));
        Assert.AreSame(previous, UpdaterText.Current);
    }

    private async Task CheckWorkerLanguageAsync(string language)
    {
        using var input = new StringReader("");
        using var output = new StringWriter();
        var interaction = new ConsoleUpdateInteraction(language, input: input, output: output);
        var update = new UpdateArguments("package.zip", "sha256:" + new string('A', 64), ".",
            int.MaxValue, language, []);

        var result = await UpdateWorker.RunAsync(update, interaction, TestContext.CancellationTokenSource.Token);

        Assert.AreEqual(1, result);
        Assert.Contains(UpdaterText.ForLanguage(language).WorkspaceNotPrepared, output.ToString());
        var otherLanguage = language == "en" ? "zh-CN" : "en";
        Assert.DoesNotContain(UpdaterText.ForLanguage(otherLanguage).WorkspaceNotPrepared, output.ToString());
    }
}
