using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities.Updater;
using SyncClipboard.Updater;

namespace SyncClipboard.Test.Client.Startup;

[TestClass]
public class SpecifiedDataDirectoryUpdateTests
{
    public TestContext TestContext { get; set; } = null!;
    private string directory = null!;

    [TestInitialize]
    public void Initialize() => directory = Directory.CreateTempSubdirectory("update profile 中文 ").FullName;

    [TestCleanup]
    public void Cleanup() => Directory.Delete(directory, recursive: true);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void UpdaterHandoff_PreservesStartupModeAcrossElevation(bool specifiedDirectory)
    {
        var profile = specifiedDirectory ? Path.Combine(directory, "profile 中文") : null;
        var options = new AppLaunchOptions(profile);
        var programDirectory = Path.Combine(directory, "SyncClipboard.app", "Contents", "MonoBundle");
        Directory.CreateDirectory(programDirectory);
        File.WriteAllText(Path.Combine(directory, "SyncClipboard.app", "Contents", "Info.plist"), "bundle");
        var request = new UpdateInstallRequest(Path.Combine(directory, "package"), "sha256:" + new string('A', 64));
        var start = FileReplacementPackageInstaller.CreateStartInfo(request, Path.Combine(directory, "workspace"),
            programDirectory, Path.Combine(directory, "SyncClipboard.AppImage"), options);
        var parsed = UpdateArguments.Parse(start.ArgumentList.ToArray());
        Assert.AreEqual(profile, parsed.AppDataDirectory);
        var elevated = UpdateArguments.Parse((parsed with { Elevated = true }).ToCommandLine().ToArray());
        Assert.AreEqual(profile, elevated.AppDataDirectory);
        Assert.IsTrue(elevated.Elevated);
    }

    [TestMethod]
    public void UpdaterHandoff_RejectsEmptyOrDuplicateDataDirectory()
    {
        var update = Arguments(Path.Combine(directory, "app"));
        var args = update.ToCommandLine().ToArray();
        Assert.ThrowsExactly<ArgumentException>(() => UpdateArguments.Parse([.. args, "--app-data-dir", " "]));
        Assert.ThrowsExactly<ArgumentException>(() => UpdateArguments.Parse(
            [.. args, "--app-data-dir", directory, "--app-data-dir", directory]));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void InstallerHandoff_PreservesSpecifiedDirectory(bool specifiedDirectory)
    {
        var profile = specifiedDirectory ? Path.Combine(directory, "profile 中文") : null;
        var start = InnoSetupInstaller.CreateStartInfo(Path.Combine(directory, "installer.exe"), new(profile));
        Assert.IsTrue(start.UseShellExecute);
        if (profile is null)
            Assert.IsEmpty(start.ArgumentList);
        else
            CollectionAssert.AreEqual(new[] { "/APPDATADIR=" + Path.Combine(profile, ".") }, start.ArgumentList.ToArray());
    }

    [TestMethod]
    [TestCategory("PlatformMacOS")]
    [TestCategory("PlatformLinux")]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UpdateOrRollback_RestartsWithSpecifiedDirectory(bool rollback)
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            Assert.Inconclusive("Requires Unix process launching.");
        var target = Path.Combine(directory, "SyncClipboard.AppImage");
        var executable = target;
        if (OperatingSystem.IsMacOS())
        {
            target = Path.Combine(directory, "SyncClipboard.app");
            var contents = Directory.CreateDirectory(Path.Combine(target, "Contents")).FullName;
            executable = Path.Combine(Directory.CreateDirectory(Path.Combine(contents, "MacOS")).FullName, "Recorder");
            File.WriteAllText(Path.Combine(contents, "Info.plist"),
                "<?xml version=\"1.0\"?><plist version=\"1.0\"><dict><key>CFBundleExecutable</key><string>Recorder</string>"
                + "<key>CFBundleIdentifier</key><string>test.syncclipboard." + Guid.NewGuid().ToString("N")
                + "</string><key>CFBundlePackageType</key><string>APPL</string></dict></plist>");
        }
        var marker = Path.Combine(directory, "arguments");
        var escapedMarker = marker.Replace("'", "'\\''");
        File.WriteAllText(executable, $"#!/bin/sh\nprintf '%s\\n' \"$@\" > '{escapedMarker}.tmp'\nmv '{escapedMarker}.tmp' '{escapedMarker}'\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var profile = Path.Combine(directory, "profile 中文");
        await UpdateWorker.RestartAsync(Arguments(target) with { AppDataDirectory = profile }, updateCompleted: !rollback);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationTokenSource.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        while (!File.Exists(marker))
            await Task.Delay(50, timeout.Token);
        string[] expected = rollback ? ["--app-data-dir", profile] : ["--update-completed", "--app-data-dir", profile];
        CollectionAssert.AreEqual(expected, File.ReadAllLines(marker));
    }

    private UpdateArguments Arguments(string target)
        => new(Path.Combine(directory, "package"), "sha256:" + new string('A', 64), target,
            Environment.ProcessId, "en", []);
}
