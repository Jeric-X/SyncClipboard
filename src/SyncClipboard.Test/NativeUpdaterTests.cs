using SyncClipboard.Updater;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;

namespace SyncClipboard.Test;

[TestClass]
public class NativeUpdaterTests
{
    public TestContext TestContext { get; set; } = null!;
    private string directory = null!;
    private string target = null!;
    private string stage = null!;
    private bool startedNativeUpdater;

    [TestInitialize]
    public void Initialize()
    {
        directory = Directory.CreateTempSubdirectory("SyncClipboard native 中文 ' ").FullName;
        // /var is a macOS symlink; use its physical path when testing Windows' no-links policy.
        if (OperatingSystem.IsMacOS() && directory.StartsWith("/var/", StringComparison.Ordinal)) directory = "/private" + directory;
        target = Directory.CreateDirectory(Path.Combine(directory, "target")).FullName;
        stage = Directory.CreateDirectory(Path.Combine(directory, "stage")).FullName;
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (startedNativeUpdater)
        {
            foreach (var process in Process.GetProcessesByName("SyncClipboard.Updater"))
            {
                using (process)
                {
                    try
                    {
                        if (process.MainModule?.FileName.StartsWith(directory + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase) != true) continue;
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(5000);
                    }
                    catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
                    { TestContext.WriteLine(error.Message); }
                }
            }
            foreach (var log in Directory.EnumerateFiles(directory, "install.log", SearchOption.AllDirectories))
                TestContext.WriteLine(File.ReadAllText(log));
        }
        Directory.Delete(directory, true);
    }

    [TestMethod]
    public void Arguments_RoundTripPathsDigestAndProtection()
    {
        var expected = Arguments() with
        {
            WorkDirectory = Path.Combine(directory, "work"),
            Elevated = true,
            LauncherId = 1234,
            ProcessStartTime = DateTime.UtcNow.Ticks
        };
        var actual = UpdateArguments.Parse(expected.ToCommandLine().ToArray());
        Assert.AreEqual(expected with { ProtectedPaths = actual.ProtectedPaths }, actual);
        CollectionAssert.AreEqual(expected.ProtectedPaths, actual.ProtectedPaths);
    }

    [TestMethod]
    public void Arguments_RejectInvalidAndDuplicateOptions()
    {
        var args = Arguments().ToCommandLine().ToArray();
        Assert.Throws<ArgumentException>(() => UpdateArguments.Parse([.. args, "--digest", "sha256:bad"]));
        Assert.Throws<ArgumentException>(() => UpdateArguments.Parse([.. args, "--unknown", "value"]));
        Assert.Throws<ArgumentException>(() => UpdateArguments.Parse((Arguments() with { Digest = "sha256:bad" }).ToCommandLine().ToArray()));
        Assert.Throws<ArgumentException>(() => UpdateArguments.Parse((Arguments() with { Executable = Path.Combine(directory, "other.exe") })
            .ToCommandLine().ToArray()));
    }

    [TestMethod]
    [DataRow("../escape.txt")]
    [DataRow("..\\escape.txt")]
    [DataRow("/absolute.txt")]
    [DataRow("C:/absolute.txt")]
    [DataRow("file:stream")]
    [DataRow("folder./file")]
    [DataRow("folder /file")]
    [DataRow("NUL.txt")]
    [DataRow("COM1")]
    [DataRow("LPT¹.txt")]
    [DataRow("a//b")]
    public async Task Extraction_RejectsUnsafeWindowsPaths(string name)
    {
        var zip = CreateZip((name, "bad"));
        await Assert.ThrowsAsync<InvalidDataException>(() => WindowsZipPackage.ExtractAsync(zip, stage, target, [],
            TestContext.CancellationTokenSource.Token));
        Assert.IsFalse(File.Exists(Path.Combine(directory, "escape.txt")));
    }

    [TestMethod]
    public async Task Extraction_RejectsDuplicateNamesAndSymbolicLinks()
    {
        var zip = CreateZip(("a.txt", "first"), ("A.txt", "second"));
        await Assert.ThrowsAsync<InvalidDataException>(() => WindowsZipPackage.ExtractAsync(zip, stage, target, [],
            TestContext.CancellationTokenSource.Token));
        File.Delete(zip);
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("link");
            entry.ExternalAttributes = 0xA000 << 16;
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => WindowsZipPackage.ExtractAsync(zip, stage, target, [],
            TestContext.CancellationTokenSource.Token));
    }

    [TestMethod]
    public async Task Extraction_ProtectsPortableAndCustomUserData()
    {
        var zip = CreateZip(("appdata/history.db", "bad"), ("StaticConfig.json", "bad"), ("SyncClipboard.json", "bad"),
            ("custom/data.txt", "bad"), ("library.dll", "new"));
        await WindowsZipPackage.ExtractAsync(zip, stage, target, WindowsZipPackage.GetProtectedPaths(Arguments()),
            TestContext.CancellationTokenSource.Token);
        Assert.HasCount(1, Directory.GetFiles(stage));
        Assert.IsTrue(File.Exists(Path.Combine(stage, "library.dll")));
        Assert.IsEmpty(Directory.GetDirectories(stage));
    }

    [TestMethod]
    public async Task Preparation_CopiesBeforeVerifyingAndDoesNotTouchInstallation()
    {
        var zip = CreateZip(("library.dll", "new"));
        var attempt = Directory.CreateDirectory(Path.Combine(directory, "attempt")).FullName;
        await Assert.ThrowsAsync<InvalidDataException>(() => WindowsZipPackage.PrepareAsync(Arguments() with { PackagePath = zip },
            attempt, TestContext.CancellationTokenSource.Token));
        CollectionAssert.AreEqual(File.ReadAllBytes(zip), File.ReadAllBytes(Path.Combine(attempt, "package.zip")));
        Assert.IsEmpty(Directory.GetFileSystemEntries(target));
        Assert.IsFalse(Directory.Exists(Path.Combine(attempt, "payload")));
    }

    [TestMethod]
    public async Task HashVerification_AcceptsExpectedDigestAndRejectsChangedSnapshot()
    {
        var zip = CreateZip(("library.dll", "new"));
        var hash = Digest(zip);
        await WindowsZipPackage.VerifyHashAsync(zip, hash.ToLowerInvariant(), TestContext.CancellationTokenSource.Token);
        File.AppendAllText(zip, "changed");
        await Assert.ThrowsAsync<InvalidDataException>(() => WindowsZipPackage.VerifyHashAsync(zip, hash,
            TestContext.CancellationTokenSource.Token));
    }

    [TestMethod]
    public void VersionValidation_RejectsDifferentReleases()
    {
        WindowsZipPackage.ValidateVersion("3.3.0+commit", "v3.3.0");
        WindowsZipPackage.ValidateVersion("3.3.0-beta1+commit", "v3.3.0-beta1");
        Assert.Throws<InvalidDataException>(() => WindowsZipPackage.ValidateVersion("3.3.0", "v3.3.0-beta1"));
        Assert.Throws<InvalidDataException>(() => WindowsZipPackage.ValidateVersion(null, "v3.3.0"));
    }

    [TestMethod]
    public void TargetValidation_RejectsProtectedRootAndLinkedDestinations()
    {
        Assert.Throws<IOException>(() => WindowsZipPackage.ValidateTarget(Arguments() with { ProtectedPaths = [directory] }));
        if (OperatingSystem.IsWindows()) return; // Windows symlink creation requires a separate privilege.
        var link = Path.Combine(target, "link");
        Directory.CreateSymbolicLink(link, stage);
        Assert.Throws<IOException>(() => FileReplacement.ValidateDestination(Path.Combine(link, "new.txt"), target, []));
    }

    [TestMethod]
    public async Task Replacement_PreservesUnrelatedFilesAndIgnoresProgressFailures()
    {
        File.WriteAllText(Path.Combine(target, "library.dll"), "old");
        File.WriteAllText(Path.Combine(target, "user.txt"), "keep");
        File.WriteAllText(Path.Combine(stage, "library.dll"), "new");
        await ApplyAsync((_, _) => throw new IOException("progress is unavailable"), TestContext.CancellationTokenSource.Token);
        Assert.AreEqual("new", File.ReadAllText(Path.Combine(target, "library.dll")));
        Assert.AreEqual("keep", File.ReadAllText(Path.Combine(target, "user.txt")));
        Assert.AreEqual("old", File.ReadAllText(Path.Combine(directory, "backup", "library.dll")));
        Assert.IsEmpty(Directory.GetFiles(target, ".syncclipboard-*"));
    }

    [TestMethod]
    public async Task Cancellation_RestoresOldFilesAndRemovesNewFilesAndDirectories()
    {
        File.WriteAllText(Path.Combine(target, "a.txt"), "old");
        File.WriteAllText(Path.Combine(stage, "a.txt"), "new");
        Directory.CreateDirectory(Path.Combine(stage, "b"));
        File.WriteAllText(Path.Combine(stage, "b", "new.txt"), "new");
        File.WriteAllText(Path.Combine(stage, "z.txt"), "new");
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAsync<OperationCanceledException>(() => ApplyAsync((phase, percent) =>
        {
            if (phase == "installing" && percent >= 66) cancel.Cancel();
        }, cancel.Token));
        Assert.AreEqual("old", File.ReadAllText(Path.Combine(target, "a.txt")));
        Assert.IsFalse(Directory.Exists(Path.Combine(target, "b")));
        Assert.IsFalse(File.Exists(Path.Combine(target, "z.txt")));
    }

    [TestMethod]
    public async Task LockedDestination_RollsBackEarlierChangesWithoutTouchingLockedFile()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows file sharing test.");
        foreach (var name in new[] { "a.txt", "z.txt" })
        {
            File.WriteAllText(Path.Combine(target, name), "old");
            File.WriteAllText(Path.Combine(stage, name), "new");
        }
        using var locked = new FileStream(Path.Combine(target, "z.txt"), FileMode.Open, FileAccess.Read, FileShare.Read);
        await Assert.ThrowsAsync<IOException>(() => ApplyAsync((_, _) => { }, TestContext.CancellationTokenSource.Token));
        Assert.AreEqual("old", File.ReadAllText(Path.Combine(target, "a.txt")));
        Assert.AreEqual("old", File.ReadAllText(Path.Combine(target, "z.txt")));
    }

    [TestMethod]
    public async Task PermissionFailure_RestoresFilesBeforeRequestingElevation()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows file permissions test.");
            return;
        }
        foreach (var name in new[] { "a.txt", "z.txt" })
        {
            File.WriteAllText(Path.Combine(target, name), "old");
            File.WriteAllText(Path.Combine(stage, name), "new");
        }
        var protectedFile = new FileInfo(Path.Combine(target, "z.txt"));
        var original = protectedFile.GetAccessControl();
        var denied = protectedFile.GetAccessControl();
        using var identity = WindowsIdentity.GetCurrent();
        denied.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.WriteData, AccessControlType.Deny));
        protectedFile.SetAccessControl(denied);
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ApplyAsync((_, _) => { }, TestContext.CancellationTokenSource.Token));
            Assert.AreEqual("old", File.ReadAllText(Path.Combine(target, "a.txt")));
            Assert.AreEqual("old", File.ReadAllText(protectedFile.FullName));
        }
        finally { protectedFile.SetAccessControl(original); }
    }

    [TestMethod]
    public async Task FailedRollback_PreservesBackupAndReportsRecoveryRequired()
    {
        File.WriteAllText(Path.Combine(target, "a.txt"), "old");
        File.WriteAllText(Path.Combine(stage, "a.txt"), "new");
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAsync<UpdateRecoveryException>(() => ApplyAsync((phase, _) =>
        {
            if (phase != "installing") return;
            File.Delete(Path.Combine(target, "a.txt"));
            Directory.CreateDirectory(Path.Combine(target, "a.txt"));
            cancel.Cancel();
        }, cancel.Token));
        Assert.AreEqual("old", File.ReadAllText(Path.Combine(directory, "backup", "a.txt")));
    }

    [TestMethod]
    public async Task Cleanup_DeletesOnlyValidatedUpdaterWorkspace()
    {
        await Assert.ThrowsAsync<IOException>(() => UpdateWorker.CleanupAsync(target, int.MaxValue, 0));
        var workspace = Path.Combine(directory, "SyncClipboard-updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, ".syncclipboard-update"), "SyncClipboard updater workspace v1");
        File.WriteAllText(Path.Combine(workspace, "backup"), "old");
        await UpdateWorker.CleanupAsync(workspace, int.MaxValue, 0);
        Assert.IsFalse(Directory.Exists(workspace));
        Assert.IsTrue(Directory.Exists(target));
    }

    [TestMethod]
    public async Task ParentWait_HonorsCancellationAndDoesNotWaitForReusedPid()
    {
        using var current = Process.GetCurrentProcess();
        await UpdateWorker.WaitForProcessAsync(current.Id, current.StartTime.ToUniversalTime().Ticks + 1, CancellationToken.None);
        await Assert.ThrowsAsync<OperationCanceledException>(() => UpdateWorker.WaitForProcessAsync(current.Id, 0,
            new CancellationToken(true)));
    }

    [TestMethod]
    public async Task NativeAotUpdater_RelocatesInstallsRestartsAndCleansWorkspace()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows NativeAOT process integration test.");
        var native = Environment.GetEnvironmentVariable("SYNC_CLIPBOARD_UPDATER_TEST_EXE");
        if (native is null) Assert.Inconclusive("Publish the NativeAOT updater and set SYNC_CLIPBOARD_UPDATER_TEST_EXE.");
        Assert.IsTrue(File.Exists(native), "The CI-published updater is missing.");
        foreach (var name in new[] { "SyncClipboard.exe", "SyncClipboard.Updater.exe" })
        {
            File.Copy(native, Path.Combine(target, name));
            File.Copy(native, Path.Combine(stage, name));
        }
        const string packageName = "SyncClipboard_win_x64_portable.zip";
        File.WriteAllText(Path.Combine(stage, "update_info.json"), JsonSerializer.Serialize(new
        { UpdateInfo = new { manage_type = "manual", update_src = "github", package_name = packageName } }));
        File.WriteAllText(Path.Combine(target, "StaticConfig.json"), "keep config");
        File.WriteAllText(Path.Combine(stage, "StaticConfig.json"), "bad config");
        File.WriteAllText(Path.Combine(stage, "installed.txt"), "new version");
        var zip = Path.Combine(directory, packageName);
        ZipFile.CreateFromDirectory(stage, zip);
        var arguments = Arguments() with
        {
            PackagePath = zip,
            Digest = Digest(zip),
            Version = FileVersionInfo.GetVersionInfo(native).ProductVersion!
        };
        var start = UpdateWorker.CreateStartInfo(Path.Combine(target, "SyncClipboard.Updater.exe"), arguments);
        start.Environment["TEMP"] = directory;
        start.Environment["TMP"] = directory;
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.CreateNoWindow = true;
        startedNativeUpdater = true;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.CancellationTokenSource.Token);
        var errors = process.StandardError.ReadToEndAsync(TestContext.CancellationTokenSource.Token);
        await process.WaitForExitAsync(TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(0, process.ExitCode);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationTokenSource.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        var workRoot = Path.Combine(directory, "SyncClipboard-updates");
        while (!File.Exists(Path.Combine(target, "installed.txt")) || Directory.GetDirectories(workRoot).Length != 0)
            await Task.Delay(100, timeout.Token);
        Assert.AreEqual("new version", File.ReadAllText(Path.Combine(target, "installed.txt")));
        Assert.AreEqual("keep config", File.ReadAllText(Path.Combine(target, "StaticConfig.json")));
        TestContext.WriteLine(await output);
        TestContext.WriteLine(await errors);
    }

    private Task ApplyAsync(Action<string, int> progress, CancellationToken token = default)
        => FileReplacement.ApplyAsync(stage, target, Path.Combine(directory, "backup"), [], progress, token);

    private UpdateArguments Arguments() => new(Path.Combine(directory, "package 中文.zip"), "sha256:" + new string('A', 64),
        target, Path.Combine(target, "SyncClipboard.exe"), "v3.3.0", int.MaxValue, "zh-CN", [Path.Combine(target, "custom")]);

    private string CreateZip(params (string Name, string Content)[] entries)
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

    private static string Digest(string path) => "sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
