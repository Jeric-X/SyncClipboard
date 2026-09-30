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
using System.ComponentModel;
using System.Reflection;
using System.Security.Cryptography;

namespace SyncClipboard.Test;

[TestClass]
public class UpdateInstallationTests
{
    public TestContext TestContext { get; set; } = null!;

    private string directory = null!;

    [TestInitialize]
    public void Initialize() => directory = Directory.CreateTempSubdirectory("SyncClipboard update 中文 ").FullName;

    [TestCleanup]
    public void Cleanup() => Directory.Delete(directory, true);

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
        if (cached) await File.WriteAllBytesAsync(Path.Combine(directory, "test.package"), bytes,
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
    public void WorkerArguments_PreservePathsAndDigestWithoutReadingPackage()
    {
        var updater = Path.Combine(directory, "missing updater");
        var target = Path.Combine(directory, "target with spaces 中文");
        var installer = new FileReplacementPackageInstaller(updater, target);
        var package = Path.Combine(directory, "package with spaces 中文.zip");
        var digest = "sha256:" + new string('B', 64);
        var request = new UpdateInstallRequest(package, digest, "v9.0.0");

        var start = installer.CreateStartInfo(request);
        var arguments = start.ArgumentList.ToArray();

        Assert.AreEqual(updater, start.FileName);
        Assert.IsFalse(start.UseShellExecute);
        Assert.AreEqual(string.Empty, start.Arguments);
        Assert.AreEqual(package, arguments[Array.IndexOf(arguments, "--package-path") + 1]);
        Assert.AreEqual(digest, arguments[Array.IndexOf(arguments, "--digest") + 1]);
        Assert.AreEqual(target, arguments[Array.IndexOf(arguments, "--target") + 1]);
        Assert.AreEqual(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            arguments[Array.IndexOf(arguments, "--process-id") + 1]);
        Assert.IsEmpty(Directory.GetFileSystemEntries(directory));
    }

    [TestMethod]
    public async Task MissingUpdater_ReportsLaunchFailureWithoutCreatingFiles()
    {
        var installer = new FileReplacementPackageInstaller(Path.Combine(directory, "missing updater"), directory);
        var request = new UpdateInstallRequest(Path.Combine(directory, "package"), "sha256:unused", "v9.0.0");

        await Assert.ThrowsAsync<Win32Exception>(() => installer.StartAsync(request, CancellationToken.None));

        Assert.IsEmpty(Directory.GetFileSystemEntries(directory));
    }

    [TestMethod]
    public async Task CanceledLaunch_DoesNotStartUpdater()
    {
        var installer = new FileReplacementPackageInstaller(Path.Combine(directory, "missing updater"), directory);
        var request = new UpdateInstallRequest(Path.Combine(directory, "package"), "sha256:unused", "v9.0.0");

        await Assert.ThrowsAsync<OperationCanceledException>(() => installer.StartAsync(request, new CancellationToken(true)));

        Assert.IsEmpty(Directory.GetFileSystemEntries(directory));
    }

    private UpdateChecker CreateChecker(IUpdateInstaller? installer, IHttp? http = null, Mock<IUpdateInstallerFactory>? factory = null)
    {
        var path = Path.Combine(directory, "update_info.json");
        File.WriteAllText(path, "{\"UpdateInfo\":{\"manage_type\":\"manual\",\"update_src\":\"github\",\"package_name\":\"test.package\"}}");
        factory ??= new Mock<IUpdateInstallerFactory>();
        factory.Setup(f => f.Create(It.IsAny<UpdateInfoConfig>())).Returns(installer);
        return new UpdateChecker(null!, http!, Mock.Of<ILogger>(), null!, Mock.Of<INotificationManager>(), null!, null!, factory.Object, new ConfigBase(path));
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
    [DataRow("manual", "github", "SyncClipboard_win_x64_portable.zip")]
    [DataRow("manual", "github", "unknown.package")]
    [DataRow("manual", "github", "")]
    [DataRow("manual", "homebrew", "SyncClipboard_win_x64_portable.zip")]
    [DataRow("external", "github", "SyncClipboard_win_x64_portable.zip")]
    [DataRow("market", "github", "SyncClipboard_win_x64_portable.zip")]
    public void Factory_ReturnsNullForUnimplementedPackagesAndChannels(
        string manageType, string source, string packageName)
    {
        var factory = new UpdateInstallerFactory();

        var installer = factory.Create(new UpdateInfoConfig
        {
            ManageType = manageType,
            UpdateSrc = source,
            PackageName = packageName
        });

        Assert.IsNull(installer);
        // Selection reports functionality without probing files or installation permissions.
        Assert.IsEmpty(Directory.GetFileSystemEntries(directory));
    }

    [TestMethod]
    public void Factory_FromDependencyInjectionKeepsUnimplementedInstallationDisabled()
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

        Assert.IsNull(installer);
        Assert.IsNull(provider.GetService<IUpdateInstaller>());
    }

    [TestMethod]
    public async Task AsyncAction_StillAllowsCancelWhileDownloading()
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new AboutViewModel.UpdateStatusViewModel { Action = _ => pending.Task };
        var download = viewModel.RunActionCommand.ExecuteAsync(null);
        var canceled = false;
        viewModel.Action = _ => { canceled = true; pending.SetResult(); return Task.CompletedTask; };
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
