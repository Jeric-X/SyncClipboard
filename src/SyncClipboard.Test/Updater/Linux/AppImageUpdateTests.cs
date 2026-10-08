using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Utilities.Updater;
using SyncClipboard.Updater;
using SyncClipboard.Updater.AppImage;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace SyncClipboard.Test.Updater.Linux;

[TestClass]
[TestCategory("PlatformLinux")]
[SupportedOSPlatform("linux")]
public class AppImageUpdateTests
{
    public TestContext TestContext { get; set; } = null!;
    private string directory = null!;
    private CancellationToken Token => TestContext.CancellationTokenSource.Token;

    [TestInitialize]
    public void Initialize()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Inconclusive("Requires Linux.");
        directory = Directory.CreateTempSubdirectory("SyncClipboard AppImage 中文 ' ").FullName;
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (directory is not null)
            Directory.Delete(directory, true);
    }

    [TestMethod]
    public async Task Handoff_CopiesNativeLibrariesAndRemovesOldMountPaths()
    {
        foreach (var file in new[] { "SyncClipboard.Updater", "libSkiaSharp.so", "libHarfBuzzSharp.so" })
        {
            File.WriteAllText(Path.Combine(directory, file), file);
        }
        var workspace = await FileReplacementPackageInstaller.PrepareUnixUpdaterAsync(FileReplacementPackageInstaller.LinuxUpdaterFiles.GetFiles(directory), Token);
        try
        {
            foreach (var file in FileReplacementPackageInstaller.LinuxUpdaterFiles.Libraries)
            {
                Assert.AreEqual(file, File.ReadAllText(Path.Combine(workspace, file)));
            }
            Assert.AreEqual("SyncClipboard.Updater", File.ReadAllText(Path.Combine(workspace, "SyncClipboard.Updater")));
            if (!OperatingSystem.IsWindows())
                Assert.IsTrue(File.GetUnixFileMode(Path.Combine(workspace, "SyncClipboard.Updater")).HasFlag(UnixFileMode.UserExecute));
            if (OperatingSystem.IsWindows())
                return;
            var start = new ProcessStartInfo();
            start.Environment["APPDIR"] = directory;
            start.Environment["APPIMAGE"] = Path.Combine(directory, "original.AppImage");
            start.Environment["LD_LIBRARY_PATH"] = Path.Combine(directory, "usr/lib") + ":/usr/lib";
            start.Environment["PATH"] = Path.Combine(directory, "usr/bin") + ":/usr/bin";
            FileReplacementPackageInstaller.LinuxUpdaterFiles.ConfigureStartInfo(start);
            Assert.AreEqual("/usr/lib", start.Environment["LD_LIBRARY_PATH"]);
            Assert.AreEqual("/usr/bin", start.Environment["PATH"]);
            Assert.IsFalse(start.Environment.ContainsKey("APPDIR"));
            Assert.IsFalse(start.Environment.ContainsKey("APPIMAGE"));
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    [TestMethod]
    public async Task Handoff_CopiesPortableRuntimeWithExecutablePermissions()
    {
        string[] names = ["SyncClipboard.Updater", "appimage-ld.so", "libSkiaSharp.so", "libHarfBuzzSharp.so", "libc.so.6"];
        foreach (var name in names)
        {
            File.WriteAllText(Path.Combine(directory, name), name);
        }
        File.SetUnixFileMode(Path.Combine(directory, "appimage-ld.so"), UnixFileMode.UserRead | UnixFileMode.UserExecute);
        File.WriteAllLines(Path.Combine(directory, FileReplacementPackageInstaller.LinuxUpdaterFiles.RuntimeManifest),
            ["appimage-ld.so", "libc.so.6"]);
        var workspace = await FileReplacementPackageInstaller.PrepareUnixUpdaterAsync(
            FileReplacementPackageInstaller.LinuxUpdaterFiles.GetFiles(directory), Token);
        try
        {
            foreach (var name in names)
            {
                Assert.AreEqual(name, File.ReadAllText(Path.Combine(workspace, name)));
            }
            Assert.IsTrue(File.GetUnixFileMode(Path.Combine(workspace, "appimage-ld.so")).HasFlag(UnixFileMode.UserExecute));
            Assert.IsTrue(File.Exists(Path.Combine(workspace, FileReplacementPackageInstaller.LinuxUpdaterFiles.RuntimeManifest)));
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    [TestMethod]
    [DataRow("../outside.so")]
    [DataRow("/tmp/outside.so")]
    [DataRow("missing.so")]
    public void Handoff_RejectsIncompleteOrEscapingPortableRuntime(string library)
    {
        foreach (var name in new[] { "SyncClipboard.Updater", "appimage-ld.so", "libSkiaSharp.so", "libHarfBuzzSharp.so" })
        {
            File.WriteAllText(Path.Combine(directory, name), name);
        }
        File.WriteAllLines(Path.Combine(directory, FileReplacementPackageInstaller.LinuxUpdaterFiles.RuntimeManifest),
            ["appimage-ld.so", library]);
        Assert.IsEmpty(FileReplacementPackageInstaller.LinuxUpdaterFiles.GetFiles(directory));
    }

    [TestMethod]
    public async Task Replacement_InstallsExecutableAtOriginalPathAndCleansBackup()
    {
        var target = Path.Combine(directory, "my clipboard.AppImage");
        var source = Path.Combine(directory, "update.AppImage");
        File.WriteAllText(target, "old");
        File.WriteAllBytes(source, ImageBytes());
        var replacement = new AppImageReplacement(target, Path.Combine(directory, "backup", "old.AppImage"), false);
        await replacement.ApplyAsync(source, new Interaction(), Token);
        CollectionAssert.AreEqual(ImageBytes(), File.ReadAllBytes(target));
        Assert.AreEqual("old", File.ReadAllText(replacement.Backup));
        if (!OperatingSystem.IsWindows())
            Assert.IsTrue(File.GetUnixFileMode(target).HasFlag(UnixFileMode.UserExecute));
        await replacement.CleanupAsync(new Interaction());
        Assert.IsFalse(File.Exists(replacement.Backup));
    }

    [TestMethod]
    public async Task FailureBeforeReplacement_LeavesOriginalAndDoesNotOfferRollback()
    {
        var target = Path.Combine(directory, "installed.AppImage");
        var source = Path.Combine(directory, "source.AppImage");
        File.WriteAllText(target, "old");
        File.WriteAllBytes(source, ImageBytes());
        var replacement = new AppImageReplacement(target, Path.Combine(directory, "backup", "old.AppImage"), false);
        var interaction = new Interaction
        {
            OnReport = phase =>
            {
                if (phase == "installing")
                    File.Delete(source);
            },
            OnFailure = (_, _, canRollback, _) =>
            {
                Assert.IsFalse(canRollback);
                Assert.AreEqual("old", File.ReadAllText(target));
                return Task.FromResult(UpdateFailureAction.Abort);
            },
            OnRollbackAvailability = available => Assert.IsFalse(available)
        };
        await Assert.ThrowsAsync<UpdateAbortedException>(() => replacement.ApplyAsync(source, interaction, Token));
        Assert.AreEqual("old", File.ReadAllText(target));
    }

    [TestMethod]
    [DataRow("Retry")]
    [DataRow("Abort")]
    [UnsupportedOSPlatform("windows")]
    public async Task PreparationFailure_RetriesOrKeepsTheOriginalImage(string choice)
    {
        if (OperatingSystem.IsWindows())
            Assert.Inconclusive("Requires Unix file permissions.");
        var target = Path.Combine(directory, "installed.AppImage");
        var source = Path.Combine(directory, "source.AppImage");
        File.WriteAllText(target, "old");
        File.WriteAllBytes(source, ImageBytes());
        var replacement = new AppImageReplacement(target, Path.Combine(directory, "backup", "old.AppImage"), false);
        var sourceMode = File.GetUnixFileMode(source);
        var prompts = 0;
        var rollbackAvailable = false;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var interaction = new Interaction
        {
            OnReport = phase =>
            {
                if (phase == "installing" && prompts == 0)
                    File.SetUnixFileMode(source, UnixFileMode.None);
            },
            OnFailure = (_, _, canRollback, _) =>
            {
                prompts++;
                Assert.IsFalse(canRollback);
                Assert.IsFalse(rollbackAvailable);
                Assert.AreEqual("old", File.ReadAllText(target));
                File.SetUnixFileMode(source, sourceMode);
                return Task.FromResult(Enum.Parse<UpdateFailureAction>(choice));
            },
            OnRollbackAvailability = available => rollbackAvailable = available
        };
        if (choice == "Retry")
        {
            await replacement.ApplyAsync(source, interaction, cancel.Token);
            CollectionAssert.AreEqual(ImageBytes(), File.ReadAllBytes(target));
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() => replacement.ApplyAsync(source, interaction, cancel.Token));
            Assert.AreEqual("old", File.ReadAllText(target));
            Assert.AreEqual("old", File.ReadAllText(replacement.Backup));
        }
        Assert.AreEqual(1, prompts);
        Assert.IsFalse(rollbackAvailable);
        Assert.IsEmpty(Directory.GetFileSystemEntries(directory, ".SyncClipboard-update-*"));
    }

    [TestMethod]
    public async Task CancellationAfterReplacement_RestoresTheOriginalImage()
    {
        var target = Path.Combine(directory, "installed.AppImage");
        var source = Path.Combine(directory, "source.AppImage");
        File.WriteAllText(target, "old");
        File.WriteAllBytes(source, ImageBytes());
        var replacement = new AppImageReplacement(target, Path.Combine(directory, "backup", "old.AppImage"), false);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var interaction = new Interaction
        {
            OnRollbackAvailability = available =>
            {
                if (available)
                {
                    CollectionAssert.AreEqual(ImageBytes(), File.ReadAllBytes(target));
                    cancel.Cancel();
                }
            }
        };
        await Assert.ThrowsAsync<IOException>(() => replacement.ApplyAsync(source, interaction, cancel.Token));
        Assert.AreEqual("old", File.ReadAllText(target));
        Assert.AreEqual("old", File.ReadAllText(replacement.Backup));
        Assert.IsEmpty(Directory.GetFileSystemEntries(directory, ".SyncClipboard-update-*"));
    }

    [TestMethod]
    public async Task CommandCancellation_StopsTheRunningProcessBeforeReturning()
    {
        if (OperatingSystem.IsWindows())
            Assert.Inconclusive("Requires a Unix child process.");
        using var process = Process.Start(new ProcessStartInfo("/bin/sleep", "60") { UseShellExecute = false })!;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        try
        {
            var wait = LinuxCommand.WaitForExitAsync(process, cancel.Token);
            cancel.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => wait.WaitAsync(TimeSpan.FromSeconds(5), Token));
            Assert.IsTrue(process.HasExited);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    [DoNotParallelize]
    public async Task AppImageInstallation_RestartsUpdatedOrRolledBackApplication(bool rollback)
    {
        var tool = Environment.GetEnvironmentVariable("SYNC_CLIPBOARD_APPIMAGE_TEST_TOOL");
        if (string.IsNullOrEmpty(tool))
            Assert.Inconclusive("Set SYNC_CLIPBOARD_APPIMAGE_TEST_TOOL to appimagetool.");
        var target = await CreateAppImageAsync(tool, "old");
        var original = File.ReadAllBytes(target);
        var package = await CreateAppImageAsync(tool, "new");
        var workspace = Directory.CreateDirectory(Path.Combine(directory, "SyncClipboard-updates", Guid.NewGuid().ToString("N"))).FullName;
        File.WriteAllText(Path.Combine(workspace, ".syncclipboard-update"), "SyncClipboard updater workspace v1");
        var update = Arguments(package, target) with { WorkDirectory = workspace };
        var attempt = Directory.CreateDirectory(Path.Combine(workspace, "attempt")).FullName;
        var payload = await AppImagePackage.PrepareAsync(update, attempt, Token);
        var replacement = new AppImageReplacement(target, Path.Combine(workspace, "backup", "old.AppImage"), false);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var interaction = new Interaction
        {
            OnRollbackAvailability = available =>
            {
                if (rollback && available)
                    cancellation.Cancel();
            }
        };
        if (rollback)
            await Assert.ThrowsAsync<IOException>(() => replacement.ApplyAsync(payload, interaction, cancellation.Token));
        else
            await replacement.ApplyAsync(payload, interaction, cancellation.Token);
        var originalExtract = Environment.GetEnvironmentVariable("APPIMAGE_EXTRACT_AND_RUN");
        try
        {
            // Exercise the real AppImage runtime without requiring a FUSE mount in CI.
            Environment.SetEnvironmentVariable("APPIMAGE_EXTRACT_AND_RUN", "1");
            await UpdateWorker.RestartAsync(update, updateCompleted: !rollback);
            var marker = Path.Combine(directory, "restarted");
            var wait = Stopwatch.StartNew();
            while (!File.Exists(marker) && wait.Elapsed < TimeSpan.FromSeconds(10))
            {
                await Task.Delay(100, Token);
            }
            string[] expected = rollback ? ["old"] : ["new", StartArguments.UpdateCompleted];
            CollectionAssert.AreEqual(expected, File.ReadAllLines(marker));
            CollectionAssert.AreEqual(rollback ? original : File.ReadAllBytes(package), File.ReadAllBytes(target));
            Assert.IsTrue(File.GetUnixFileMode(target).HasFlag(UnixFileMode.UserExecute));
            Assert.AreEqual(0, await UpdateWorker.CleanupAndReportAsync(update, interaction, appImageReplacement: replacement));
            Assert.IsFalse(Directory.Exists(workspace));
            Assert.IsFalse(File.Exists(replacement.Backup));
            Assert.IsEmpty(Directory.GetFileSystemEntries(directory, ".SyncClipboard-update-*"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("APPIMAGE_EXTRACT_AND_RUN", originalExtract);
        }
    }

    private async Task<string> CreateAppImageAsync(string tool, string version)
    {
        var appDir = Directory.CreateDirectory(Path.Combine(directory, version + ".AppDir")).FullName;
        File.WriteAllText(Path.Combine(appDir, "AppRun"), $"#!/bin/sh\nprintf '%s\\n' {version} \"$@\" > \"$(dirname \"$APPIMAGE\")/restarted\"\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(Path.Combine(appDir, "AppRun"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.WriteAllText(Path.Combine(appDir, "test.desktop"), "[Desktop Entry]\nType=Application\nName=Updater Test\nExec=AppRun\nIcon=test\nCategories=Utility;\n");
        File.WriteAllText(Path.Combine(appDir, "test.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"32\" height=\"32\"><rect width=\"32\" height=\"32\"/></svg>");
        var package = Path.Combine(directory, version + ".AppImage");
        var start = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--appimage-extract-and-run");
        start.ArgumentList.Add(appDir);
        start.ArgumentList.Add(package);
        start.Environment["ARCH"] = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "aarch64" : "x86_64";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(Token);
        var errors = process.StandardError.ReadToEndAsync(Token);
        await process.WaitForExitAsync(Token);
        Assert.AreEqual(0, process.ExitCode, await output + await errors);
        return package;
    }

    private static UpdateArguments Arguments(string package, string target)
        => new(package, "sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(package))), target, int.MaxValue, "en", []);

    private static byte[] ImageBytes()
    {
        var bytes = new byte[128];
        new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1, 0, (byte)'A', (byte)'I', 2 }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(18, 2), RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? (ushort)183 : (ushort)62);
        return bytes;
    }

    private sealed class Interaction : IUpdateInteraction
    {
        public Action<string>? OnReport { get; init; }
        public Action<bool>? OnRollbackAvailability { get; init; }
        public void SetRollbackAvailable(bool available) => OnRollbackAvailability?.Invoke(available);
        public UpdateFailureHandler? OnFailure { get; init; }
        public void Report(string phase, int percent) => OnReport?.Invoke(phase);
        public Task<ForceExitAction> ConfirmForceExitAsync(CancellationToken token) => Task.FromResult(ForceExitAction.No);
        public Task<UpdateFailureAction> AskFailureActionAsync(string path, Exception error, bool canRollback, CancellationToken token)
            => OnFailure?.Invoke(path, error, canRollback, token) ?? throw new AssertFailedException(path + ": " + error);
        public Task ShowResultAsync(UpdateResult result) => Task.CompletedTask;
    }
}
