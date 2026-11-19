using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Utilities.Instances;
using System.Diagnostics;
using System.IO.Pipes;

namespace SyncClipboard.Test.Client.Startup;

[TestClass]
public class AppDataDirectoryOptionsTests
{
    [TestMethod]
    public void NormalStartup_KeepsDefaultInstance()
    {
        var options = AppDataDirectoryOptions.Parse([StartArguments.ShutdownPrivious, "--command-ShowHistory"]);
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
        Assert.ThrowsExactly<ArgumentException>(() => AppDataDirectoryOptions.Parse(args));
    }

    [TestMethod]
    public void ExplicitDirectory_NormalizesRelativePathWithoutCreatingIt()
    {
        var name = "startup-" + Guid.NewGuid().ToString("N");
        var options = AppDataDirectoryOptions.Parse(["--app-data-dir", Path.Combine(name, "..", name)]);
        Assert.IsTrue(options.IsSpecifiedDataDirectory);
        Assert.AreNotEqual(AppInstance.GetInstanceSuffix(AppDataDirectoryOptions.Parse([])), AppInstance.GetInstanceSuffix(options));
        Assert.AreEqual(Path.GetFullPath(name), options.AppDataDirectory);
        Assert.IsFalse(Directory.Exists(options.AppDataDirectory));
        Assert.AreEqual(AppInstance.GetInstanceSuffix(options),
            AppInstance.GetInstanceSuffix(AppDataDirectoryOptions.Parse(["--app-data-dir=" + options.AppDataDirectory + Path.DirectorySeparatorChar])));
        Assert.AreNotEqual(AppInstance.GetInstanceSuffix(options),
            AppInstance.GetInstanceSuffix(AppDataDirectoryOptions.Parse(["--app-data-dir=" + name + "-other"])));
    }

    [TestMethod]
    public void ExplicitDirectory_ResolvesRelativePathAgainstLauncherWorkingDirectory()
    {
        var root = Directory.CreateTempSubdirectory("startup-working-directory-").FullName;
        try
        {
            var profile = Path.Combine(root, "profile 中文");
            var relative = AppDataDirectoryOptions.Parse(["--app-data-dir", "profile 中文"], root);
            var absolute = AppDataDirectoryOptions.Parse(["--app-data-dir", profile]);
            Assert.AreEqual(absolute, relative);
            Assert.IsFalse(Directory.Exists(profile));
            Assert.AreEqual(absolute, AppDataDirectoryOptions.Parse(["--app-data-dir", profile], Path.GetTempPath()));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    [DataRow("Profile")]
    [DataRow("Prófile")]
    public void ExplicitDirectory_FirstLaunchUsesStableDirectoryIdentity(string name)
    {
        var root = Directory.CreateTempSubdirectory("startup-created-").FullName;
        try
        {
            var path = Path.Combine(root, name);
            var first = AppDataDirectoryOptions.Parse(["--app-data-dir", path]);
            Assert.IsFalse(Directory.Exists(path));
            first = first.PrepareDataDirectory(Path.Combine(root, "program"));
            Assert.IsTrue(Directory.Exists(path));
            var next = AppDataDirectoryOptions.Parse(["--app-data-dir", path]);
            Assert.AreEqual(first.AppDataDirectory, next.AppDataDirectory);
            Assert.AreEqual(AppInstance.GetInstanceSuffix(first), AppInstance.GetInstanceSuffix(next));
            Assert.AreEqual(AppInstance.GetPipeName(first), AppInstance.GetPipeName(next));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void ExistingFile_RejectsDataDirectory()
    {
        var file = Path.GetTempFileName();
        try
        {
            Assert.ThrowsExactly<ArgumentException>(() => AppDataDirectoryOptions.Parse(["--app-data-dir", file]));
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
        var overlapping = AppDataDirectoryOptions.Parse(["--app-data-dir", programDirectory]);
        Assert.ThrowsExactly<ArgumentException>(() => overlapping.PrepareDataDirectory(programDirectory));
        Assert.IsFalse(Directory.Exists(programDirectory));
        var separate = AppDataDirectoryOptions.Parse(["--app-data-dir", Path.Combine(programDirectory, "profile")]);
        separate.ValidateDataDirectory(programDirectory);
        AppDataDirectoryOptions.Parse([]).ValidateDataDirectory(programDirectory);
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
            var inside = AppDataDirectoryOptions.Parse(["--app-data-dir", Path.Combine(programDirectory, "profile")]);
            Assert.ThrowsExactly<ArgumentException>(() => inside.PrepareDataDirectory(programDirectory));
            Assert.IsFalse(Directory.Exists(inside.AppDataDirectory));
            var outside = AppDataDirectoryOptions.Parse(["--app-data-dir", Path.Combine(root, "profile")]);
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
        var options = AppDataDirectoryOptions.Parse(["--app-data-dir", "test data", "--command-ShowHistory"]);
        var start = new ProcessStartInfo();
        StartArguments.AddRestartArguments(start, options);
        CollectionAssert.AreEqual(new[] { StartArguments.ShutdownPrivious, "--app-data-dir", options.AppDataDirectory },
            start.ArgumentList.ToArray());
        Assert.AreEqual(options, AppDataDirectoryOptions.Parse(start.ArgumentList.ToArray()));
    }

    [TestMethod]
    [TestCategory("PlatformWindows")]
    [TestCategory("PlatformMacOS")]
    [TestCategory("PlatformLinux")]
    public async Task InstancePipe_DeliversCommands()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            Assert.Inconclusive("Requires a supported desktop OS.");
        var options = AppDataDirectoryOptions.Parse(["--app-data-dir", "pipe-" + Guid.NewGuid().ToString("N")]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var server = new NamedPipeServerStream(AppInstance.GetPipeName(options), PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var client = new NamedPipeClientStream(".", AppInstance.GetPipeName(options), PipeDirection.Out, PipeOptions.Asynchronous);
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
    public void MacDirectoryIdentity_FollowsVolumeCaseSensitivity()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Inconclusive("Requires macOS path canonicalization.");
        var root = Directory.CreateTempSubdirectory("startup-case-").FullName;
        try
        {
            var upper = Directory.CreateDirectory(Path.Combine(root, "Profile")).FullName;
            var lower = Path.Combine(root, "profile");
            var sameDirectory = Directory.Exists(lower);
            if (!sameDirectory)
                Directory.CreateDirectory(lower);
            var first = AppDataDirectoryOptions.Parse(["--app-data-dir", upper]);
            var second = AppDataDirectoryOptions.Parse(["--app-data-dir", lower]);
            var firstChild = AppDataDirectoryOptions.Parse(["--app-data-dir", Path.Combine(upper, "new")]);
            var secondChild = AppDataDirectoryOptions.Parse(["--app-data-dir", Path.Combine(lower, "new")]);
            Assert.AreEqual(sameDirectory, AppInstance.GetInstanceSuffix(first) == AppInstance.GetInstanceSuffix(second));
            Assert.AreEqual(sameDirectory, AppInstance.GetPipeName(first) == AppInstance.GetPipeName(second));
            Assert.AreEqual(sameDirectory, AppInstance.GetInstanceSuffix(firstChild) == AppInstance.GetInstanceSuffix(secondChild));
            using var marker = AppInstance.CreateInstallationMarker(upper);
            Assert.AreEqual(sameDirectory, AppInstance.HasOtherInstances(lower, -1));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    [TestCategory("PlatformMacOS")]
    [TestCategory("PlatformLinux")]
    public void UnixPaths_PreserveCaseForDistinctProfiles()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            Assert.Inconclusive("Tests Unix path identity.");
        var upper = AppDataDirectoryOptions.Parse(["--app-data-dir", "Test"]);
        var lower = AppDataDirectoryOptions.Parse(["--app-data-dir", "test"]);
        Assert.AreNotEqual(AppInstance.GetInstanceSuffix(upper), AppInstance.GetInstanceSuffix(lower));
        Assert.AreNotEqual(AppInstance.GetPipeName(upper), AppInstance.GetPipeName(lower));
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
            var direct = AppDataDirectoryOptions.Parse(["--app-data-dir", Path.Combine(target, "new")]);
            var linked = AppDataDirectoryOptions.Parse(["--app-data-dir", Path.Combine(link, "new")]);
            Assert.AreEqual(direct, linked);
            Assert.AreEqual(AppInstance.GetInstanceSuffix(direct), AppInstance.GetInstanceSuffix(linked));
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
