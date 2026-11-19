using SyncClipboard.Core.Commons;
using System.Diagnostics;
using System.IO.Pipes;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Test.Client.Startup;

[TestClass]
public class AppLaunchOptionsTests
{
    [TestMethod]
    public void NormalStartup_KeepsDefaultInstance()
    {
        var options = AppLaunchOptions.Parse([StartArguments.ShutdownPrivious, "--command-ShowHistory"]);
        Assert.IsFalse(options.IsSpecifiedDataDirectory);
        Assert.IsNull(options.AppDataDirectory);
    }

    [TestMethod]
    [DataRow("--app-data-dir")]
    [DataRow("--app-data-dir=")]
    [DataRow("--app-data-dir", " ")]
    [DataRow("--app-data-dir", "--shutdown-previous")]
    [DataRow("--app-data-dir=a", "--app-data-dir=b")]
    public void InvalidOptions_RejectStartup(params string[] args)
    {
        Assert.ThrowsExactly<ArgumentException>(() => AppLaunchOptions.Parse(args));
    }

    [TestMethod]
    public void ExplicitDirectory_NormalizesRelativePathWithoutCreatingIt()
    {
        var name = "startup-" + Guid.NewGuid().ToString("N");
        var options = AppLaunchOptions.Parse(["--app-data-dir", Path.Combine(name, "..", name)]);
        Assert.IsTrue(options.IsSpecifiedDataDirectory);
        Assert.AreNotEqual(AppLaunchOptions.Parse([]).InstanceSuffix, options.InstanceSuffix);
        Assert.AreEqual(Path.GetFullPath(name), options.AppDataDirectory);
        Assert.IsFalse(Directory.Exists(options.AppDataDirectory));
        Assert.AreEqual(options.InstanceSuffix,
            AppLaunchOptions.Parse(["--app-data-dir=" + options.AppDataDirectory + Path.DirectorySeparatorChar]).InstanceSuffix);
        Assert.AreNotEqual(options.InstanceSuffix,
            AppLaunchOptions.Parse(["--app-data-dir=" + name + "-other"]).InstanceSuffix);
    }

    [TestMethod]
    public void ExistingFile_RejectsDataDirectory()
    {
        var file = Path.GetTempFileName();
        try
        {
            Assert.ThrowsExactly<ArgumentException>(() => AppLaunchOptions.Parse(["--app-data-dir", file]));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [TestMethod]
    public void ProgramDirectory_RejectsPortableConfigurationOverlapButAllowsSeparateSubdirectory()
    {
        var programDirectory = Path.Combine(Path.GetTempPath(), "installation-" + Guid.NewGuid().ToString("N"));
        var overlapping = AppLaunchOptions.Parse(["--app-data-dir", programDirectory]);
        Assert.ThrowsExactly<ArgumentException>(() => overlapping.ValidateDataDirectory(programDirectory));
        var separate = AppLaunchOptions.Parse(["--app-data-dir", Path.Combine(programDirectory, "profile")]);
        separate.ValidateDataDirectory(programDirectory);
        AppLaunchOptions.Parse([]).ValidateDataDirectory(programDirectory);
    }

    [TestMethod]
    [TestCategory("PlatformMacOS")]
    public void MacBundle_RejectsNestedDataDirectory()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Inconclusive("Validates macOS app bundle locations.");
        var root = Directory.CreateTempSubdirectory("startup-bundle-").FullName;
        try
        {
            var contents = Directory.CreateDirectory(Path.Combine(root, "SyncClipboard.app", "Contents")).FullName;
            File.WriteAllText(Path.Combine(contents, "Info.plist"), "test");
            var programDirectory = Directory.CreateDirectory(Path.Combine(contents, "MonoBundle")).FullName;
            var inside = AppLaunchOptions.Parse(["--app-data-dir", Path.Combine(programDirectory, "profile")]);
            Assert.ThrowsExactly<ArgumentException>(() => inside.ValidateDataDirectory(programDirectory));
            var outside = AppLaunchOptions.Parse(["--app-data-dir", Path.Combine(root, "profile")]);
            outside.ValidateDataDirectory(programDirectory);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Restart_PreservesOnlyInstanceArgumentsAndDirectoryWithSpaces()
    {
        var options = AppLaunchOptions.Parse(["--app-data-dir", "test data", "--command-ShowHistory"]);
        var start = new ProcessStartInfo();
        options.AddRestartArguments(start);
        CollectionAssert.AreEqual(new[] { StartArguments.ShutdownPrivious, "--app-data-dir", options.AppDataDirectory },
            start.ArgumentList.ToArray());
        Assert.AreEqual(options, AppLaunchOptions.Parse(start.ArgumentList.ToArray()));
    }

    [TestMethod]
    [TestCategory("PlatformWindows")]
    [TestCategory("PlatformMacOS")]
    [TestCategory("PlatformLinux")]
    public async Task InstancePipe_DeliversCommands()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            Assert.Inconclusive("Requires a supported desktop OS.");
        var options = AppLaunchOptions.Parse(["--app-data-dir", "pipe-" + Guid.NewGuid().ToString("N")]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var server = new NamedPipeServerStream(options.PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var client = new NamedPipeClientStream(".", options.PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        var connection = server.WaitForConnectionAsync(timeout.Token);
        await client.ConnectAsync(timeout.Token);
        await connection;
        var write = client.WriteAsync("Active"u8.ToArray(), timeout.Token);
        var received = new byte[6];
        await server.ReadExactlyAsync(received, timeout.Token);
        await write;
        CollectionAssert.AreEqual("Active"u8.ToArray(), received);
    }

    [TestMethod]
    [TestCategory("PlatformWindows")]
    [TestCategory("PlatformMacOS")]
    [TestCategory("PlatformLinux")]
    public void DataDirectoryLock_PreventsConcurrentUseAndAllowsReuseAfterExit()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            Assert.Inconclusive("Requires a supported desktop OS.");
        var root = Directory.CreateTempSubdirectory("startup-lock-").FullName;
        try
        {
            using (AppInstance.LockDataDirectory(root))
            {
                Assert.ThrowsExactly<IOException>(() => AppInstance.LockDataDirectory(root).Dispose());
            }
            using var nextInstance = AppInstance.LockDataDirectory(root);
            Assert.IsTrue(nextInstance.CanWrite);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    [TestCategory("PlatformWindows")]
    [TestCategory("PlatformMacOS")]
    [TestCategory("PlatformLinux")]
    public void InstallationMarker_ExcludesCallerAndTracksOtherProcesses()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            Assert.Inconclusive("Requires a supported desktop OS.");
        var directory = Path.Combine(Path.GetTempPath(), "installation-" + Guid.NewGuid().ToString("N"));
        using (var first = AppInstance.CreateInstallationMarker(directory))
        {
            using (AppInstance.CreateInstallationMarker(directory))
            {
                Assert.IsFalse(AppInstance.HasOtherInstances(directory, Environment.ProcessId));
                Assert.IsTrue(AppInstance.HasOtherInstances(directory, -1));
                Assert.IsFalse(AppInstance.HasOtherInstances(directory + "-other", -1));
            }
            Assert.IsTrue(AppInstance.HasOtherInstances(directory, -1));
        }
        Assert.IsFalse(AppInstance.HasOtherInstances(directory, -1));
    }

    [TestMethod]
    [TestCategory("PlatformMacOS")]
    [TestCategory("PlatformLinux")]
    public void UnixPaths_PreserveCaseForDistinctProfiles()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            Assert.Inconclusive("Tests Unix path identity.");
        var upper = AppLaunchOptions.Parse(["--app-data-dir", "Test"]);
        var lower = AppLaunchOptions.Parse(["--app-data-dir", "test"]);
        Assert.AreNotEqual(upper.InstanceSuffix, lower.InstanceSuffix);
        Assert.AreNotEqual(upper.PipeName, lower.PipeName);
    }

    [TestMethod]
    [TestCategory("PlatformMacOS")]
    [TestCategory("PlatformLinux")]
    public void SymbolicLink_UsesTargetInstance()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            Assert.Inconclusive("Symbolic link setup requires Unix.");
        var root = Directory.CreateTempSubdirectory("startup-").FullName;
        try
        {
            var target = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
            var link = Path.Combine(root, "link");
            Directory.CreateSymbolicLink(link, target);
            var direct = AppLaunchOptions.Parse(["--app-data-dir", Path.Combine(target, "new")]);
            var linked = AppLaunchOptions.Parse(["--app-data-dir", Path.Combine(link, "new")]);
            Assert.AreEqual(direct, linked);
            Assert.AreEqual(direct.InstanceSuffix, linked.InstanceSuffix);
            using (AppInstance.CreateInstallationMarker(target))
                Assert.IsTrue(AppInstance.HasOtherInstances(link, -1));
            var image = Path.Combine(target, "SyncClipboard.AppImage");
            File.WriteAllText(image, "test");
            var imageLink = Path.Combine(root, "image-link");
            File.CreateSymbolicLink(imageLink, image);
            using (AppInstance.CreateInstallationMarker(image))
                Assert.IsTrue(AppInstance.HasOtherInstances(imageLink, -1));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
