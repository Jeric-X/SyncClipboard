using Microsoft.Extensions.DependencyInjection;
using Moq;
using NativeNotification.Interface;
using SyncClipboard.Core;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Updater;
using SyncClipboard.Core.ViewModels;
using System.Reflection;
using System.Security.Cryptography;

namespace SyncClipboard.Test;

[TestClass]
public class UpdateInstallationTests
{
    public TestContext TestContext { get; set; } = null!;

    private string directory = null!;
    private ConfigurationTestServices configurationServices = null!;
    private ConfigManager config = null!;

    [TestInitialize]
    public void Initialize()
    {
        directory = Directory.CreateTempSubdirectory("SyncClipboard update 中文 ").FullName;
        configurationServices = new ConfigurationTestServices();
        config = new ConfigManager(Path.Combine(directory, "SyncClipboard.json"), configurationServices.Upgrader);
    }

    [TestCleanup]
    public void Cleanup()
    {
        configurationServices.Dispose();
        Directory.Delete(directory, true);
    }

    [TestMethod]
    public async Task Installing_BlocksRepeatedClicksAndChecks_AndReportsLaunchFailure()
    {
        var installer = new Mock<IUpdateInstaller>();
        var launch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        installer.Setup(i => i.StartAsync(It.IsAny<UpdateInstallRequest>(), It.IsAny<CancellationToken>())).Returns(launch.Task);
        var checker = CreateChecker(installer.Object);
        SetProperty(checker, "GithubRelease", new GitHubRelease { TagName = "v9.0.0" });
        SetProperty(checker, "GithubAsset", new GitHubRelease.GitHubAsset { Digest = "sha256:test" });
        SetDownloadedStatus(checker);
        var action = checker.CurrentState.ManualAction!;
        Assert.AreEqual(SyncClipboard.Core.I18n.Strings.InstallUpdate, checker.CurrentState.ActionText);
        var installation = action(CancellationToken.None);
        Assert.AreEqual(UpdaterState.Installing, checker.CurrentState.State);
        await action(CancellationToken.None);
        await checker.RunAutoUpdateFlow(TestContext.CancellationTokenSource.Token);
        await checker.RunUpdateFlow();
        Assert.AreEqual(UpdaterState.Installing, checker.CurrentState.State);
        installer.Verify(i => i.StartAsync(It.IsAny<UpdateInstallRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        launch.SetException(new IOException("Updater could not start"));
        await installation;
        Assert.AreEqual(UpdaterState.Failed, checker.CurrentState.State);
        Assert.AreEqual("Updater could not start", checker.CurrentState.Message);
        Assert.AreEqual(string.Empty, checker.CurrentState.ActionText);
        await checker.CurrentState.ManualAction!(CancellationToken.None);
        installer.Verify(i => i.StartAsync(It.IsAny<UpdateInstallRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PrepareWindowsUpdater_ReusesAvailableRuntimeAndResourcesWithoutRequiringMissingLibraries(bool looseAppXbf)
    {
        string[] files = ["SyncClipboard.Updater.exe", "Microsoft.UI.Xaml.dll", "Microsoft.WindowsAppRuntime.Bootstrap.dll",
            "SyncClipboard.pri", "SyncClipboard.Core.dll", "coreclr.dll"];
        if (looseAppXbf)
            files = [.. files, "App.xbf"];
        foreach (var name in files)
            await File.WriteAllTextAsync(Path.Combine(directory, name), "content of " + name, TestContext.CancellationTokenSource.Token);
        var workspace = await FileReplacementPackageInstaller.PrepareWindowsUpdaterAsync(directory,
            TestContext.CancellationTokenSource.Token);
        try
        {
            // The copied files remain readable after their originals have been removed for replacement.
            foreach (var name in files)
                File.Delete(Path.Combine(directory, name));
            Assert.AreEqual("content of SyncClipboard.Updater.exe", File.ReadAllText(Path.Combine(workspace, files[0])));
            Assert.AreEqual("content of Microsoft.UI.Xaml.dll", File.ReadAllText(Path.Combine(workspace, files[1])));
            Assert.AreEqual("content of Microsoft.WindowsAppRuntime.Bootstrap.dll", File.ReadAllText(Path.Combine(workspace, files[2])));
            Assert.AreEqual("content of SyncClipboard.pri", File.ReadAllText(Path.Combine(workspace, "resources.pri")));
            if (looseAppXbf)
                Assert.AreEqual("content of App.xbf", File.ReadAllText(Path.Combine(workspace, "App.xbf")));
            Assert.IsFalse(File.Exists(Path.Combine(workspace, "SyncClipboard.Core.dll")));
            Assert.IsFalse(File.Exists(Path.Combine(workspace, "coreclr.dll")));
            Assert.IsFalse(File.Exists(Path.Combine(workspace, "Microsoft.WindowsAppRuntime.dll")));
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    [TestMethod]
    public async Task PrepareUpdater_CopiesHelperAndCreatesWorkspaceMarker()
    {
        var source = Path.Combine(directory, "helper.exe");
        await File.WriteAllTextAsync(source, "updater", TestContext.CancellationTokenSource.Token);
        var workspace = await FileReplacementPackageInstaller.PrepareUpdaterAsync([(source, "SyncClipboard.Updater.exe")],
            TestContext.CancellationTokenSource.Token);
        try
        {
            SyncClipboard.Updater.UpdateWorker.ValidateWorkspace(workspace);
            Assert.AreEqual("updater", File.ReadAllText(Path.Combine(workspace, "SyncClipboard.Updater.exe")));
            Assert.AreEqual("updater", File.ReadAllText(source));
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    [TestMethod]
    public async Task CanceledLaunch_RestoresInstallButton()
    {
        var installer = new Mock<IUpdateInstaller>();
        installer.Setup(i => i.StartAsync(It.IsAny<UpdateInstallRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var checker = CreateChecker(installer.Object);
        SetProperty(checker, "GithubRelease", new GitHubRelease { TagName = "v9.0.0" });
        SetProperty(checker, "GithubAsset", new GitHubRelease.GitHubAsset { Digest = "sha256:test" });
        SetDownloadedStatus(checker);
        await checker.CurrentState.ManualAction!(CancellationToken.None);
        Assert.AreEqual(UpdaterState.ReadyToInstall, checker.CurrentState.State);
        Assert.AreEqual(SyncClipboard.Core.I18n.Strings.InstallUpdate, checker.CurrentState.ActionText);
        await checker.CurrentState.ManualAction!(CancellationToken.None);
        installer.Verify(i => i.StartAsync(It.IsAny<UpdateInstallRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [TestMethod]
    public async Task InstallationHandoff_LeavesAppExitToInstaller()
    {
        var installer = new Mock<IUpdateInstaller>();
        installer.Setup(i => i.StartAsync(It.IsAny<UpdateInstallRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var checker = CreateChecker(installer.Object);
        SetProperty(checker, "GithubRelease", new GitHubRelease { TagName = "v9.0.0" });
        SetProperty(checker, "GithubAsset", new GitHubRelease.GitHubAsset { Digest = "sha256:test" });
        SetDownloadedStatus(checker);

        // No AppCore exists in this fixture; handoff must not try to exit the host.
        await checker.CurrentState.ManualAction!(CancellationToken.None);

        Assert.AreEqual(UpdaterState.Installing, checker.CurrentState.State);
        installer.Verify(i => i.StartAsync(It.IsAny<UpdateInstallRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public void UnsupportedPackage_KeepsOpenFolder()
    {
        var checker = CreateChecker(null);
        SetDownloadedStatus(checker);
        Assert.AreEqual(SyncClipboard.Core.I18n.Strings.OpenFolder, checker.CurrentState.ActionText);
        Assert.AreEqual(UpdaterState.Downloaded, checker.CurrentState.State);
        Assert.AreEqual(SyncClipboard.Core.I18n.Strings.NewVersionDownloaded, checker.CurrentState.Message);
    }

    [TestMethod]
    public async Task InstallationWithoutInstaller_ReportsUnsupported()
    {
        var checker = CreateChecker(null);
        SetStatus(checker, UpdaterState.ReadyToInstall);

        await checker.CurrentState.ManualAction!(CancellationToken.None);

        Assert.AreEqual(UpdaterState.Failed, checker.CurrentState.State);
        Assert.AreEqual(SyncClipboard.Core.I18n.Strings.UpdateInstallationUnsupported, checker.CurrentState.Message);
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(true, true)]
    [DataRow(false, false)]
    [DataRow(false, true)]
    public async Task DownloadCompletion_SelectsStateForInstallationCapability(bool supported, bool cached)
    {
        var installer = new Mock<IUpdateInstaller>();
        var bytes = new byte[] { 1, 2, 3, 4 };
        var http = new Mock<IHttp>();
        http.Setup(h => h.GetFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<HttpDownloadProgress>>(),
            It.IsAny<CancellationToken?>()))
            .Returns((string url, string path, IProgress<HttpDownloadProgress>? progress, CancellationToken? token)
                => File.WriteAllBytesAsync(path, bytes, token ?? CancellationToken.None));
        var checker = CreateChecker(supported ? installer.Object : null, http.Object);
        // An absolute test version directory keeps downloads inside the temporary fixture.
        SetProperty(checker, "GithubRelease", new GitHubRelease { TagName = directory });
        SetProperty(checker, "GithubAsset", new GitHubRelease.GitHubAsset
        {
            Digest = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)),
            BrowserDownloadUrl = "https://example.invalid/update.package"
        });
        if (cached)
            await File.WriteAllBytesAsync(Path.Combine(directory, "test.package"), bytes,
            TestContext.CancellationTokenSource.Token);
        SetStatus(checker, UpdaterState.ReadyForDownload);

        await checker.CurrentState.ManualAction!(TestContext.CancellationTokenSource.Token);

        Assert.AreEqual(supported ? UpdaterState.ReadyToInstall : UpdaterState.Downloaded, checker.CurrentState.State);
        Assert.AreEqual(supported ? SyncClipboard.Core.I18n.Strings.InstallUpdate : SyncClipboard.Core.I18n.Strings.OpenFolder,
            checker.CurrentState.ActionText);
        http.Verify(h => h.GetFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<HttpDownloadProgress>>(),
            It.IsAny<CancellationToken?>()), cached ? Times.Never() : Times.Once());
    }

    [TestMethod]
    [DataRow(UpdaterState.Downloaded, true)]
    [DataRow(UpdaterState.Downloaded, false)]
    [DataRow(UpdaterState.ReadyToInstall, true)]
    [DataRow(UpdaterState.ReadyToInstall, false)]
    public void ActionMapping_IsDeterminedByState(UpdaterState state, bool supported)
    {
        var installer = new Mock<IUpdateInstaller>();
        var checker = CreateChecker(supported ? installer.Object : null);
        SetStatus(checker, state);
        Assert.AreEqual(state == UpdaterState.ReadyToInstall ? SyncClipboard.Core.I18n.Strings.InstallUpdate
            : SyncClipboard.Core.I18n.Strings.OpenFolder, checker.CurrentState.ActionText);
        Assert.IsNotNull(checker.CurrentState.ManualAction);
    }

    [TestMethod]
    [TestCategory("PlatformWindows")]
    [TestCategory("PlatformMacOS")]
    [TestCategory("PlatformLinux")]
    public void WorkerArguments_PreservePathsAndDigestWithoutReadingPackage()
    {
        var programDirectory = Path.GetFullPath(Env.ProgramDirectory);
        var target = programDirectory;
        if (OperatingSystem.IsMacOS())
        {
            target = Path.Combine(directory, "SyncClipboard.app");
            programDirectory = Path.Combine(target, "Contents", "MonoBundle");
            Directory.CreateDirectory(programDirectory);
            File.WriteAllText(Path.Combine(target, "Contents", "Info.plist"), "bundle");
        }
        else if (OperatingSystem.IsLinux())
        {
            target = Path.Combine(directory, "SyncClipboard.AppImage");
        }
        var workspace = Path.Combine(directory, "workspace");
        var updater = Path.Combine(workspace, OperatingSystem.IsWindows() ? "SyncClipboard.Updater.exe" : "SyncClipboard.Updater");
        var package = Path.Combine(directory, "package with spaces 中文.zip");
        var digest = "sha256:" + new string('B', 64);
        var request = new UpdateInstallRequest(package, digest);

        var start = FileReplacementPackageInstaller.CreateStartInfo(request, workspace, programDirectory, target);
        var arguments = start.ArgumentList.ToArray();

        Assert.AreEqual(updater, start.FileName);
        Assert.AreEqual(workspace, arguments[Array.IndexOf(arguments, "--work-dir") + 1]);
        Assert.IsFalse(start.UseShellExecute);
        Assert.AreEqual(string.Empty, start.Arguments);
        Assert.AreEqual(package, arguments[Array.IndexOf(arguments, "--package-path") + 1]);
        Assert.AreEqual(digest, arguments[Array.IndexOf(arguments, "--digest") + 1]);
        Assert.AreEqual(target, arguments[Array.IndexOf(arguments, "--target") + 1]);
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            var parsed = SyncClipboard.Updater.UpdateArguments.Parse(arguments);
            Assert.AreEqual(Path.TrimEndingDirectorySeparator(target), parsed.Target);
        }
        Assert.AreEqual(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            arguments[Array.IndexOf(arguments, "--process-id") + 1]);
        var appElevated = OperatingSystem.IsWindows() && Env.IsRunningAsAdministrator;
        Assert.AreEqual(appElevated ? "true" : "false",
            arguments[Array.IndexOf(arguments, "--app-elevated") + 1]);
        Assert.IsFalse(File.Exists(package));
        Assert.IsFalse(Directory.Exists(workspace));
    }

    [TestMethod]
    [TestCategory("PlatformWindows")]
    [TestCategory("PlatformMacOS")]
    [TestCategory("PlatformLinux")]
    public async Task CanceledLaunch_DoesNotStartUpdater()
    {
        var installer = new FileReplacementPackageInstaller();
        var request = new UpdateInstallRequest(Path.Combine(directory, "package"), "sha256:unused");

        string[] names = OperatingSystem.IsLinux()
            ? ["SyncClipboard.Updater", .. FileReplacementPackageInstaller.LinuxUpdaterFiles.Libraries]
            : ["SyncClipboard.Updater.exe"];
        var created = new List<string>();
        try
        {
            foreach (var name in names)
            {
                var path = Path.Combine(Env.ProgramDirectory, name);
                if (File.Exists(path))
                    continue;
                File.WriteAllText(path, "test updater file");
                created.Add(path);
            }
            var existingPaths = Directory.GetFileSystemEntries(directory);
            await Assert.ThrowsAsync<OperationCanceledException>(() => installer.StartAsync(request, new CancellationToken(true)));
            CollectionAssert.AreEquivalent(existingPaths, Directory.GetFileSystemEntries(directory));
        }
        finally
        {
            foreach (var path in created)
            {
                File.Delete(path);
            }
        }
    }

    private UpdateChecker CreateChecker(IUpdateInstaller? installer, IHttp? http = null,
        Mock<IUpdateInstallerFactory>? factory = null, INotificationManager? notifications = null)
    {
        var path = Path.Combine(directory, "update_info.json");
        File.WriteAllText(path, "{\"UpdateInfo\":{\"manage_type\":\"manual\",\"update_src\":\"github\",\"package_name\":\"test.package\"}}");
        factory ??= new Mock<IUpdateInstallerFactory>();
        factory.Setup(f => f.Create(It.IsAny<UpdateInfoConfig>())).Returns(installer);
        return new UpdateChecker(null!, http!, Mock.Of<ILogger>(), null!, notifications ?? Mock.Of<INotificationManager>(),
            null!, config, factory.Object, new ConfigBase(path));
    }

    [TestMethod]
    public void AutoDownloadSettingChange_AllowsCurrentVersionToNotifyAgain()
    {
        var notifications = new Mock<INotificationManager>();
        var checker = CreateChecker(Mock.Of<IUpdateInstaller>(), notifications: notifications.Object);
        SetProperty(checker, "GithubRelease", new GitHubRelease { TagName = "v9.0.0" });
        SetStatus(checker, UpdaterState.ReadyForDownload);
        var notify = typeof(UpdateChecker).GetMethod("SendNotification", BindingFlags.Instance | BindingFlags.NonPublic)!;
        notify.Invoke(checker, null);
        var count = notifications.Invocations.Count;
        Assert.IsGreaterThan(0, count);
        notify.Invoke(checker, null);
        Assert.AreEqual(count, notifications.Invocations.Count);

        var current = config.GetConfig<ProgramConfig>();
        config.SetConfig(current with { AutoDownloadUpdate = !current.AutoDownloadUpdate });
        SetDownloadedStatus(checker);
        notify.Invoke(checker, null);
        Assert.AreEqual(count * 2, notifications.Invocations.Count);
        notify.Invoke(checker, null);
        Assert.AreEqual(count * 2, notifications.Invocations.Count);
    }

    [TestMethod]
    public async Task InnoInstaller_RejectsPackageChangedAfterDownloadBeforeLaunching()
    {
        var package = Path.Combine(directory, "installer.exe");
        File.WriteAllText(package, "downloaded installer");
        var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(package)));
        File.AppendAllText(package, "changed");
        await Assert.ThrowsAsync<InvalidDataException>(() => new InnoSetupInstaller().StartAsync(
            new UpdateInstallRequest(package, digest), TestContext.CancellationTokenSource.Token));
    }

    [TestMethod]
    public void Checker_SelectsInstallerOnceFromItsUpdateConfiguration()
    {
        var factory = new Mock<IUpdateInstallerFactory>();
        var checker = CreateChecker(null, factory: factory);

        SetDownloadedStatus(checker);
        SetDownloadedStatus(checker);

        factory.Verify(f => f.Create(It.Is<UpdateInfoConfig>(info => info.ManageType == UpdateInfoConfig.TypeManual
            && info.UpdateSrc == "github" && info.PackageName == "test.package")), Times.Once);
        Assert.AreEqual(UpdaterState.Downloaded, checker.CurrentState.State);
    }

    [TestMethod]
    [DataRow("manual", "github", "SyncClipboard_win_x64_portable.zip", true, true)]
    [DataRow("manual", "github", "SyncClipboard_win_x64_portable.zip", false, false)]
    [DataRow("manual", "github", "SyncClipboard_win_arm64_portable.zip", true, true)]
    [DataRow("manual", "github", "custom-update.zip", true, true)]
    [DataRow("manual", "github", "custom-update.ZIP", true, true)]
    [DataRow("manual", "github", "custom-update.zip.txt", true, false)]
    [DataRow("manual", "github", "unknown.package", true, false)]
    [DataRow("manual", "github", "", true, false)]
    [DataRow("manual", "homebrew", "SyncClipboard_win_x64_portable.zip", true, false)]
    [DataRow("external", "github", "SyncClipboard_win_x64_portable.zip", true, false)]
    [DataRow("market", "github", "SyncClipboard_win_x64_portable.zip", true, false)]
    public void Factory_SelectsWindowsZipInstaller(
        string manageType, string source, string packageName, bool isWindows, bool supported)
    {
        File.WriteAllText(Path.Combine(directory, "SyncClipboard.Updater.exe"), "updater");
        var installer = UpdateInstallerFactory.Create(new UpdateInfoConfig
        {
            ManageType = manageType,
            UpdateSrc = source,
            PackageName = packageName
        }, isWindows, directory);

        Assert.AreEqual(supported, installer is FileReplacementPackageInstaller);
        if (!supported)
            Assert.IsNull(installer);
    }

    [TestMethod]
    [DataRow("manual", "github", "SyncClipboard_win_x64_installer.exe", true, true)]
    [DataRow("manual", "github", "SyncClipboard_win_arm64_installer.exe", true, true)]
    [DataRow("manual", "github", "custom-update.EXE", true, true)]
    [DataRow("manual", "github", "custom-update.zip.exe", true, true)]
    [DataRow("manual", "github", "SyncClipboard_win_x64_installer.exe", false, false)]
    [DataRow("manual", "github", "update.msi", true, false)]
    [DataRow("manual", "winget", "update.exe", true, false)]
    [DataRow("external", "github", "update.exe", true, false)]
    [DataRow("market", "github", "update.exe", true, false)]
    public void Factory_SelectsWindowsInnoSetupInstaller(
        string manageType, string source, string packageName, bool isWindows, bool supported)
    {
        var installer = UpdateInstallerFactory.Create(new UpdateInfoConfig
        {
            ManageType = manageType,
            UpdateSrc = source,
            PackageName = packageName
        }, isWindows, directory);

        if (supported)
            Assert.IsInstanceOfType<InnoSetupInstaller>(installer);
        else
            Assert.IsNull(installer);
    }

    [TestMethod]
    [DataRow("update.dmg", true, null, true)]
    [DataRow("update.DMG", true, null, true)]
    [DataRow("update.dmg", false, null, false)]
    [DataRow("update.zip", true, null, false)]
    [DataRow("update.dmg", true, "Info.plist", false)]
    [DataRow("update.dmg", true, "SyncClipboard.Updater", false)]
    [DataRow("update.dmg", true, "libSkiaSharp.dylib", false)]
    [DataRow("update.dmg", true, "libHarfBuzzSharp.dylib", false)]
    [DataRow("update.dmg", true, "libAvaloniaNative.dylib", false)]
    public void Factory_SelectsMacDmgInstaller(string packageName, bool isMacOS, string? missing, bool supported)
    {
        var contents = Path.Combine(directory, "SyncClipboard.app", "Contents");
        var program = Directory.CreateDirectory(Path.Combine(contents, "MonoBundle")).FullName;
        var resources = Directory.CreateDirectory(Path.Combine(contents, "Resources", "Updater")).FullName;
        string[] files =
        [
            Path.Combine(contents, "Info.plist"),
            Path.Combine(resources, "SyncClipboard.Updater"),
            .. MacUpdaterFiles.Libraries.Select(name => Path.Combine(program, name))
        ];
        foreach (var file in files)
        {
            if (Path.GetFileName(file) != missing)
                File.WriteAllText(file, "test");
        }
        var installer = UpdateInstallerFactory.Create(new UpdateInfoConfig
        {
            ManageType = UpdateInfoConfig.TypeManual,
            UpdateSrc = "github",
            PackageName = packageName
        }, false, program, isMacOS: isMacOS);

        Assert.AreEqual(supported, installer is FileReplacementPackageInstaller);
        if (!supported)
            Assert.IsNull(installer);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MissingUpdater_OffersOpenFolderInsteadOfInstallation(bool directoryAtUpdaterPath)
    {
        if (directoryAtUpdaterPath)
            Directory.CreateDirectory(Path.Combine(directory, "SyncClipboard.Updater.exe"));

        var installer = UpdateInstallerFactory.Create(new UpdateInfoConfig
        {
            ManageType = UpdateInfoConfig.TypeManual,
            UpdateSrc = "github",
            PackageName = "SyncClipboard_win_x64_portable.zip"
        }, true, directory);
        Assert.IsNull(installer);

        var checker = CreateChecker(installer);
        SetDownloadedStatus(checker);

        Assert.AreEqual(UpdaterState.Downloaded, checker.CurrentState.State);
        Assert.AreEqual(SyncClipboard.Core.I18n.Strings.OpenFolder, checker.CurrentState.ActionText);
        Assert.IsNotNull(checker.CurrentState.ManualAction);
    }

    [TestMethod]
    public void Factory_FromDependencyInjectionSelectsInstallerForCurrentPlatform()
    {
        var services = new ServiceCollection();
        AppCore.ConfigCommonService(services);
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IUpdateInstallerFactory>();
        var installer = factory.Create(new UpdateInfoConfig
        {
            ManageType = UpdateInfoConfig.TypeManual,
            UpdateSrc = "github",
            PackageName = "SyncClipboard_win_x64_portable.zip"
        });

        var supported = OperatingSystem.IsWindows()
            && File.Exists(Path.Combine(Env.ProgramDirectory, "SyncClipboard.Updater.exe"));
        Assert.AreEqual(supported, installer is FileReplacementPackageInstaller);
        Assert.IsNull(provider.GetService<IUpdateInstaller>());
    }

    [TestMethod]
    public async Task AsyncAction_StillAllowsCancelWhileDownloading()
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new AboutViewModel.UpdateStatusViewModel { Action = _ => pending.Task };
        var download = viewModel.RunActionCommand.ExecuteAsync(null);
        var canceled = false;
        viewModel.Action = _ =>
        {
            canceled = true;
            pending.SetResult();
            return Task.CompletedTask;
        };
        Assert.IsTrue(viewModel.RunActionCommand.CanExecute(null));
        await viewModel.RunActionCommand.ExecuteAsync(null);
        await download;
        Assert.IsTrue(canceled);
    }

    private static void SetProperty(UpdateChecker checker, string name, object value) => typeof(UpdateChecker)
        .GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(checker, value);

    private static void SetDownloadedStatus(UpdateChecker checker) => typeof(UpdateChecker)
        .GetMethod("SetDownloadedStatus", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(checker, null);

    private static void SetStatus(UpdateChecker checker, UpdaterState state) => typeof(UpdateChecker)
        .GetMethod("SetStatus", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(checker, [state]);
}
