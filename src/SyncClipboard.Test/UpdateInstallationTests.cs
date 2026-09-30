using Microsoft.Extensions.DependencyInjection;
using Moq;
using NativeNotification.Interface;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Updater;
using SyncClipboard.Core.Utilities.Updater.Strategies;
using SyncClipboard.Core.ViewModels;
using System.Diagnostics;
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
    [DataRow("3.3.0", "v3.3.0")]
    [DataRow("3.3.0+abcdef", "v3.3.0")]
    [DataRow("3.3.0-beta2+abcdef", "v3.3.0-beta2")]
    public void PackageVersion_AcceptsMatchingRelease(string productVersion, string releaseVersion)
        => UpdatePackageVerifier.ValidateVersion(productVersion, releaseVersion);

    [TestMethod]
    [DataRow(null, "v3.3.0")]
    [DataRow("", "v3.3.0")]
    [DataRow("3.2.0+abcdef", "v3.3.0")]
    [DataRow("3.3.0-beta1", "v3.3.0-beta2")]
    [DataRow("3.3.0-beta2", "v3.3.0")]
    [DataRow("3.3.0", "invalid")]
    public void PackageVersion_RejectsMissingOrMismatchedRelease(string? productVersion, string releaseVersion)
        => Assert.Throws<InvalidDataException>(() => UpdatePackageVerifier.ValidateVersion(productVersion, releaseVersion));

    [TestMethod]
    [DataRow(100, 100, 0)]
    [DataRow(100, 50, 0)]
    [DataRow(100, 150, 50)]
    public void ReplacementSpace_ChargesOnlyNetGrowth(int oldSize, int newSize, int expectedGrowth)
    {
        var target = Path.Combine(directory, "old");
        var stage = Path.Combine(directory, "new");
        File.WriteAllBytes(target, new byte[oldSize]);
        File.WriteAllBytes(stage, new byte[newSize]);
        IFileReplacementStrategy strategy = new RecordingReplacementStrategy();
        Assert.AreEqual((long)expectedGrowth, strategy.GetRequiredInstallationSpace(new UpdateInstallTask
        {
            Directory = directory,
            Kind = "test",
            Target = target,
            Stage = stage,
            Backup = "unused",
            Executable = "unused",
            Version = "v9.0.0",
            ProcessId = Environment.ProcessId
        }));
    }

    [TestMethod]
    public async Task HashCheck_RejectsChangedPackage()
    {
        var file = Path.Combine(directory, "package");
        await File.WriteAllTextAsync(file, "original", TestContext.CancellationTokenSource.Token);
        var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file, TestContext.CancellationTokenSource.Token)));
        await UpdatePackageVerifier.VerifyHashAsync(file, digest.ToLowerInvariant(), CancellationToken.None);
        await File.WriteAllTextAsync(file, "modified", TestContext.CancellationTokenSource.Token);
        await Assert.ThrowsAsync<InvalidDataException>(() => UpdatePackageVerifier.VerifyHashAsync(file, digest, CancellationToken.None));
    }

    [TestMethod]
    public async Task Installing_BlocksRepeatedClicksAndChecks_AndReportsPreparationFailure()
    {
        var installer = new Mock<IUpdateInstaller>();
        installer.SetupGet(i => i.RequiresAppExit).Returns(true);
        installer.Setup(i => i.GetCapability())
            .Returns(new UpdateInstallCapability(UpdatePackageKind.FileReplacement, directory));
        var prepared = new TaskCompletionSource<UpdateInstallTask>(TaskCreationOptions.RunContinuationsAsynchronously);
        installer.Setup(i => i.PrepareAsync(It.IsAny<UpdateInstallRequest>(), It.IsAny<CancellationToken>())).Returns(prepared.Task);
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
        installer.Verify(i => i.PrepareAsync(It.IsAny<UpdateInstallRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        prepared.SetException(new IOException("Disk full"));
        await installation;
        Assert.AreEqual(UpdaterState.Failed, checker.CurrentState.State);
        Assert.AreEqual("Disk full", checker.CurrentState.Message);
        Assert.AreEqual(string.Empty, checker.CurrentState.ActionText);
        await checker.CurrentState.ManualAction!(CancellationToken.None);
        installer.Verify(i => i.PrepareAsync(It.IsAny<UpdateInstallRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    [DataRow(UpdatePackageKind.FileReplacement)]
    public async Task CanceledAuthorization_RestoresInstallButton(UpdatePackageKind kind)
    {
        var installer = new Mock<IUpdateInstaller>();
        installer.SetupGet(i => i.RequiresAppExit).Returns(true);
        installer.Setup(i => i.GetCapability())
            .Returns(new UpdateInstallCapability(kind, directory));
        installer.Setup(i => i.PrepareAsync(It.IsAny<UpdateInstallRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateInstallTask
            {
                Directory = directory,
                Kind = kind.ToString(),
                Target = directory,
                Stage = "unused",
                Backup = "unused",
                Executable = "unused",
                Version = "v9.0.0",
                ProcessId = Environment.ProcessId
            });
        installer.Setup(i => i.StartAsync(It.IsAny<UpdateInstallTask>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var checker = CreateChecker(installer.Object);
        SetProperty(checker, "GithubRelease", new GitHubRelease { TagName = "v9.0.0" });
        SetProperty(checker, "GithubAsset", new GitHubRelease.GitHubAsset { Digest = "sha256:test" });
        SetDownloadedStatus(checker);
        await checker.CurrentState.ManualAction!(CancellationToken.None);
        Assert.AreEqual(UpdaterState.ReadyToInstall, checker.CurrentState.State);
        Assert.AreEqual(SyncClipboard.Core.I18n.Strings.InstallUpdate, checker.CurrentState.ActionText);
        await checker.CurrentState.ManualAction!(CancellationToken.None);
        installer.Verify(i => i.StartAsync(It.IsAny<UpdateInstallTask>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [TestMethod]
    public async Task InstallationHandoff_RespectsInstallerExitRequirement()
    {
        var installer = new Mock<IUpdateInstaller>();
        installer.SetupGet(i => i.RequiresAppExit).Returns(false);
        installer.Setup(i => i.GetCapability())
            .Returns(new UpdateInstallCapability(UpdatePackageKind.FileReplacement, directory));
        installer.Setup(i => i.PrepareAsync(It.IsAny<UpdateInstallRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateInstallTask
            {
                Directory = directory,
                Kind = nameof(UpdatePackageKind.FileReplacement),
                Target = directory,
                Stage = "unused",
                Backup = "unused",
                Executable = "unused",
                Version = "v9.0.0",
                ProcessId = Environment.ProcessId
            });
        installer.Setup(i => i.StartAsync(It.IsAny<UpdateInstallTask>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var checker = CreateChecker(installer.Object);
        SetProperty(checker, "GithubRelease", new GitHubRelease { TagName = "v9.0.0" });
        SetProperty(checker, "GithubAsset", new GitHubRelease.GitHubAsset { Digest = "sha256:test" });
        SetDownloadedStatus(checker);

        // No AppCore exists in this fixture; handoff must not try to exit the host.
        await checker.CurrentState.ManualAction!(CancellationToken.None);

        Assert.AreEqual(UpdaterState.Installing, checker.CurrentState.State);
        installer.Verify(i => i.StartAsync(It.IsAny<UpdateInstallTask>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public void UnsupportedPackage_KeepsOpenFolder()
    {
        var installer = new Mock<IUpdateInstaller>();
        installer.SetupGet(i => i.RequiresAppExit).Returns(true);
        installer.Setup(i => i.GetCapability()).Returns(new UpdateInstallCapability(UpdatePackageKind.Unsupported, ""));
        var checker = CreateChecker(installer.Object);
        SetDownloadedStatus(checker);
        Assert.AreEqual(SyncClipboard.Core.I18n.Strings.OpenFolder, checker.CurrentState.ActionText);
        Assert.AreEqual(UpdaterState.Downloaded, checker.CurrentState.State);
    }

    [TestMethod]
    [DataRow(UpdatePackageKind.FileReplacement, false)]
    [DataRow(UpdatePackageKind.FileReplacement, true)]
    [DataRow(UpdatePackageKind.Unsupported, false)]
    [DataRow(UpdatePackageKind.Unsupported, true)]
    public async Task DownloadCompletion_SelectsStateForInstallationCapability(UpdatePackageKind kind, bool cached)
    {
        var installer = new Mock<IUpdateInstaller>();
        installer.SetupGet(i => i.RequiresAppExit).Returns(true);
        installer.Setup(i => i.GetCapability()).Returns(new UpdateInstallCapability(kind, directory));
        var bytes = new byte[] { 1, 2, 3, 4 };
        var http = new Mock<IHttp>();
        http.Setup(h => h.GetFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<HttpDownloadProgress>>(),
            It.IsAny<CancellationToken?>()))
            .Returns((string url, string path, IProgress<HttpDownloadProgress>? progress, CancellationToken? token)
                => File.WriteAllBytesAsync(path, bytes, token ?? CancellationToken.None));
        var checker = CreateChecker(installer.Object, http.Object);
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

        var supported = kind != UpdatePackageKind.Unsupported;
        Assert.AreEqual(supported ? UpdaterState.ReadyToInstall : UpdaterState.Downloaded, checker.CurrentState.State);
        Assert.AreEqual(supported ? SyncClipboard.Core.I18n.Strings.InstallUpdate : SyncClipboard.Core.I18n.Strings.OpenFolder,
            checker.CurrentState.ActionText);
        http.Verify(h => h.GetFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IProgress<HttpDownloadProgress>>(),
            It.IsAny<CancellationToken?>()), cached ? Times.Never() : Times.Once());
    }

    [TestMethod]
    [DataRow(UpdaterState.Downloaded, UpdatePackageKind.FileReplacement)]
    [DataRow(UpdaterState.Downloaded, UpdatePackageKind.Unsupported)]
    [DataRow(UpdaterState.ReadyToInstall, UpdatePackageKind.FileReplacement)]
    [DataRow(UpdaterState.ReadyToInstall, UpdatePackageKind.Unsupported)]
    public void ActionMapping_IsDeterminedByState(UpdaterState state, UpdatePackageKind kind)
    {
        var installer = new Mock<IUpdateInstaller>();
        installer.SetupGet(i => i.RequiresAppExit).Returns(true);
        installer.Setup(i => i.GetCapability()).Returns(new UpdateInstallCapability(kind, directory));
        var checker = CreateChecker(installer.Object);
        SetStatus(checker, state);
        Assert.AreEqual(state == UpdaterState.ReadyToInstall ? SyncClipboard.Core.I18n.Strings.InstallUpdate
            : SyncClipboard.Core.I18n.Strings.OpenFolder, checker.CurrentState.ActionText);
        Assert.IsNotNull(checker.CurrentState.ManualAction);
    }

    private ServiceCollection CreateServices(string packageName)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IAppConfig>());
        var path = Path.Combine(directory, "factory-update-info.json");
        File.WriteAllText(path, "{\"UpdateInfo\":{\"manage_type\":\"manual\",\"update_src\":\"github\",\"package_name\":\"test.package\"}}");
        var config = new ConfigBase(path);
        config.SetConfig(new UpdateInfoConfig { ManageType = UpdateInfoConfig.TypeManual, UpdateSrc = "github", PackageName = packageName });
        services.AddKeyedSingleton(Env.UpdateInfoFile, config);
        services.AddUpdateInstallation();
        return services;
    }

    [TestMethod]
    [DataRow("SyncClipboard_win_x64_portable.zip")]
    [DataRow("SyncClipboard_win_arm64_portable.zip")]
    [DataRow("SyncClipboard_win_x64_installer.exe")]
    [DataRow("SyncClipboard_macos_arm64.dmg")]
    [DataRow("SyncClipboard_linux_x64.AppImage")]
    [DataRow("SyncClipboard_linux_x64.deb")]
    [DataRow("SyncClipboard_linux_x64.rpm")]
    public void InstallerRegistration_KeepsManualInstallationForAllPackages(string packageName)
    {
        using var provider = CreateServices(packageName).BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        var installer = provider.GetRequiredService<IUpdateInstaller>();
        Assert.IsInstanceOfType<UnsupportedUpdateInstaller>(installer);
        Assert.IsFalse(installer.GetCapability().Supported);
        Assert.IsFalse(installer.RequiresAppExit);
        var checker = CreateChecker(installer);
        SetDownloadedStatus(checker);
        Assert.AreEqual(UpdaterState.Downloaded, checker.CurrentState.State);
        Assert.AreEqual(SyncClipboard.Core.I18n.Strings.OpenFolder, checker.CurrentState.ActionText);
        var config = provider.GetRequiredKeyedService<ConfigBase>(Env.UpdateInfoFile);
        config.SetConfig(new UpdateInfoConfig { ManageType = UpdateInfoConfig.TypeExternal });
        Assert.AreSame(installer, provider.GetRequiredService<IUpdateInstaller>());
    }

    [TestMethod]
    public async Task FileReplacement_VerifiesSnapshotAndUsesOnlyItsSelectedStrategy()
    {
        var strategy = new RecordingReplacementStrategy();
        var installer = new FileReplacementUpdater(strategy, new UpdateTaskCleaner(Path.Combine(directory, "tasks"), "1.0.0"));
        var package = Path.Combine(directory, "package");
        await File.WriteAllTextAsync(package, "package contents", TestContext.CancellationTokenSource.Token);
        var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(
            await File.ReadAllBytesAsync(package, TestContext.CancellationTokenSource.Token)));
        var request = new UpdateInstallRequest(package, digest, "v9.0.0", new(strategy.Kind, directory));
        var prepared = await installer.PrepareAsync(request, TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(1, strategy.PrepareCount);
        Assert.AreNotEqual(package, prepared.Stage);
        Assert.AreEqual("package contents", File.ReadAllText(Path.Combine(prepared.Stage, "package")));
        Assert.IsTrue(File.Exists(Path.Combine(prepared.Directory, "task.json")));

        // The selected instance rejects a different kind; it cannot dispatch to another strategy.
        var wrongRequest = request with { Capability = new(UpdatePackageKind.Unsupported, directory) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.PrepareAsync(wrongRequest, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.StartAsync(
            prepared with { Kind = nameof(UpdatePackageKind.Unsupported) }, CancellationToken.None));
        Assert.AreEqual(1, strategy.PrepareCount);
        Assert.AreEqual(0, strategy.StartCount);

        // Startup failure must still abort handoff without starting a real process in this fixture.
        await Assert.ThrowsAsync<IOException>(() => installer.StartAsync(prepared, CancellationToken.None));
        Assert.AreEqual(1, strategy.StartCount);
        Assert.IsTrue(File.Exists(Path.Combine(prepared.Directory, "cancel")));
        Assert.IsFalse(File.Exists(Path.Combine(prepared.Directory, "commit")));

        await File.WriteAllTextAsync(package, "corrupted", TestContext.CancellationTokenSource.Token);
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.PrepareAsync(request, CancellationToken.None));
        Assert.AreEqual(1, strategy.PrepareCount);
        Assert.HasCount(1, Directory.GetDirectories(Path.Combine(directory, "tasks")));
    }

    private sealed class RecordingReplacementStrategy : IFileReplacementStrategy
    {
        public UpdatePackageKind Kind => UpdatePackageKind.FileReplacement;
        public int PrepareCount { get; private set; }
        public int StartCount { get; private set; }

        public UpdateInstallCapability GetCapability() => new(Kind, "unused");
        public string GetInstallationDirectory(string target) => target;

        public Task<UpdateInstallTask> PreparePayloadAsync(UpdateInstallRequest request, string work, CancellationToken token)
        {
            PrepareCount++;
            var stage = Directory.CreateDirectory(Path.Combine(work, "payload")).FullName;
            File.Copy(request.PackagePath, Path.Combine(stage, "package"));
            return Task.FromResult(new UpdateInstallTask
            {
                Directory = work,
                Kind = Kind.ToString(),
                Target = request.Capability.TargetPath,
                Stage = stage,
                Backup = Path.Combine(work, "backup"),
                Executable = "unused",
                Version = request.Version,
                ProcessId = Environment.ProcessId
            });
        }

        public ProcessStartInfo CreateWorkerStartInfo(UpdateInstallTask update)
        {
            StartCount++;
            throw new IOException("Worker launch failed.");
        }
    }

    private UpdateChecker CreateChecker(IUpdateInstaller installer, IHttp? http = null)
    {
        var path = Path.Combine(directory, "update_info.json");
        File.WriteAllText(path, "{\"UpdateInfo\":{\"manage_type\":\"manual\",\"update_src\":\"github\",\"package_name\":\"test.package\"}}");
        return new UpdateChecker(null!, http!, Mock.Of<ILogger>(), null!, Mock.Of<INotificationManager>(), null!, null!, installer, new ConfigBase(path));
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

    [TestMethod]
    public async Task HelperReadiness_ReportsFailureWithoutCommitting()
    {
        var update = new UpdateInstallTask
        {
            Directory = directory,
            Kind = "FileReplacement",
            Target = "unused",
            Stage = "unused",
            Backup = "unused",
            Executable = "unused",
            Version = "v9.0.0",
            ProcessId = Environment.ProcessId
        };
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        File.WriteAllText(Path.Combine(directory, "failed"), "Helper failed to initialize.");
        await Assert.ThrowsAsync<IOException>(() => FileReplacementUpdater.WaitForReadyAsync(update, process, CancellationToken.None));
        Assert.IsFalse(File.Exists(Path.Combine(directory, "commit")));
    }

    private static void SetProperty(UpdateChecker checker, string name, object value) => typeof(UpdateChecker)
        .GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(checker, value);

    private static void SetDownloadedStatus(UpdateChecker checker) => typeof(UpdateChecker)
        .GetMethod("SetDownloadedStatus", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(checker, null);

    private static void SetStatus(UpdateChecker checker, UpdaterState state) => typeof(UpdateChecker)
        .GetMethod("SetStatus", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(checker, [state]);
}
