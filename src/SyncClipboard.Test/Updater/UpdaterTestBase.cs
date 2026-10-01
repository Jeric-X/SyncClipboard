using SyncClipboard.Updater;
using SyncClipboard.Updater.Zip;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace SyncClipboard.Test.Updater;

public abstract class UpdaterTestBase
{
    public TestContext TestContext { get; set; } = null!;
    private protected string directory = null!;
    private protected string target = null!;
    private protected string stage = null!;

    [TestInitialize]
    public void Initialize()
    {
        directory = Directory.CreateTempSubdirectory("SyncClipboard native 中文 ' ").FullName;
        target = Directory.CreateDirectory(Path.Combine(directory, "target")).FullName;
        stage = Directory.CreateDirectory(Path.Combine(directory, "stage")).FullName;
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(directory, true);

    private protected static Process StartWaitingProcess()
    {
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/sleep")
        { UseShellExecute = false, CreateNoWindow = true };
        if (OperatingSystem.IsWindows())
        {
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-Command");
            start.ArgumentList.Add("Start-Sleep -Seconds 30");
        }
        else
            start.ArgumentList.Add("30");
        return Process.Start(start)!;
    }

    private protected Task ApplyAsync(Action<string, int> progress, CancellationToken token = default)
        => FileReplacement.ApplyAsync(stage, target, Path.Combine(directory, "backup"), [], progress, token);

    private protected UpdateArguments Arguments() => new(Path.Combine(directory, "package 中文.zip"), "sha256:" + new string('A', 64),
        target, int.MaxValue, "zh-CN", [Path.Combine(target, "custom")]);

    private protected string CreateZip(params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(directory, "test.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write(content);
        }
        return path;
    }

    private protected static string Digest(string path) => "sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
