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
    public void Text_UsesSelectedLanguageAndFallsBackToEnglish(string language, bool chinese)
    {
        var text = UpdaterText.ForLanguage(language);
        Assert.AreEqual(chinese ? "安装更新" : "Install the update", text.Installing);
        Assert.Contains(chinese ? "是否强制退出" : "Force it to exit", text.ConfirmForceExit);
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
        var interaction = new RecordingUpdateInteraction();
        var update = new UpdateArguments("package.zip", "sha256:" + new string('A', 64), ".",
            int.MaxValue, language, []);

        var result = await UpdateWorker.RunAsync(update, interaction, TestContext.CancellationTokenSource.Token);

        Assert.AreEqual(1, result);
        Assert.Contains(UpdaterText.ForLanguage(language).WorkspaceNotPrepared, interaction.Result!.Error!);
        var otherLanguage = language == "en" ? "zh-CN" : "en";
        Assert.DoesNotContain(UpdaterText.ForLanguage(otherLanguage).WorkspaceNotPrepared, interaction.Result!.Error!);
    }
}
