using Microsoft.Extensions.DependencyInjection;
using NativeNotification.Interface;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Runner;
using SyncClipboard.Core.ViewModels;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using static SyncClipboard.Core.Utilities.Updater.GitHubRelease;

namespace SyncClipboard.Core.Utilities.Updater;

using CancelableTask = Func<CancellationToken, Task>;

public class UpdateChecker : IStateMachine<UpdaterStatus>
{
    public UpdaterStatus CurrentState { get; private set; }
    public HttpDownloadProgress DownloadProgress { get; private set; }

    private bool NeedUpdate { get; set; } = false;
    private GitHubRelease? GithubRelease { get; set; } = null;
    private GitHubAsset? GithubAsset { get; set; } = null;
    private string DownloadPath
    {
        get
        {
            var versionFolder = Path.Combine(Env.UpdateFolder, GithubRelease?.TagName ?? "latest");
            return Path.Combine(versionFolder, updateInfo.PackageName);
        }
    }

    private readonly GithubUpdater githubUpdater;
    private readonly IHttp http;
    private readonly ILogger logger;
    private readonly LocalClipboardSetter localClipboardSetter;
    private readonly IMainWindow mainWindow;
    private readonly ConfigManager configManager;
    private readonly IUpdateInstaller? updateInstaller;
    private readonly Lock installationGate = new();
    private bool installing;

    private readonly INotificationManager notificationManager;
    private readonly UpdateInfoConfig updateInfo;

    private readonly SingletonTask singletonTask = new();

    public event Action<UpdaterStatus>? StateChanged;
    public event Action<HttpDownloadProgress>? DownloadProgressChanged;

    public UpdateChecker(
        GithubUpdater githubUpdater,
        IHttp http,
        ILogger logger,
        LocalClipboardSetter localClipboardSetter,
        INotificationManager notification,
        IMainWindow mainWindow,
        ConfigManager configManager,
        IUpdateInstallerFactory updateInstallerFactory,
        [FromKeyedServices(Env.UpdateInfoFile)] ConfigBase updateInfoConfig)
    {
        this.githubUpdater = githubUpdater;
        this.http = http;
        this.logger = logger;
        this.localClipboardSetter = localClipboardSetter;
        this.configManager = configManager;
        this.notificationManager = notification;
        this.mainWindow = mainWindow;
        updateInfo = updateInfoConfig.GetConfig<UpdateInfoConfig>();
        updateInstaller = updateInstallerFactory.Create(updateInfo);
        var autoDownload = configManager.GetConfig<ProgramConfig>().AutoDownloadUpdate;
        configManager.ListenConfig<ProgramConfig>(config =>
        {
            lock (installationGate)
            {
                if (autoDownload == config.AutoDownloadUpdate)
                    return;
                autoDownload = config.AutoDownloadUpdate;
                if (GithubRelease?.TagName is { } version)
                    notifiedVersion.Remove(version);
            }
        });
        SetStatus(UpdaterState.Idle);
        logger.WriteAsync(updateInfo.ToString());
    }

    private CancelableTask SingletonTask(CancelableTask task)
    {
        return async token =>
        {
            try
            {
                Task operation;
                lock (installationGate)
                {
                    if (installing)
                        return;
                    operation = singletonTask.Run(task, token);
                }
                await operation;
            }
            catch (OperationCanceledException)
            {
                SetStatus(UpdaterState.Canceled);
            }
            catch (Exception ex)
            {
                await logger.WriteAsync(ex.Message);
                SetErrorState(ex.Message);
            }
        };
    }

    private async Task InstallUpdate(CancellationToken token)
    {
        Task operation;
        lock (installationGate)
        {
            if (installing || CurrentState.State != UpdaterState.ReadyToInstall)
                return;
            installing = true;
            operation = singletonTask.Run(InstallUpdateCore, token);
        }
        try
        {
            await operation;
        }
        finally
        {
            lock (installationGate)
                installing = false;
        }
    }

    private async Task InstallUpdateCore(CancellationToken token)
    {
        try
        {
            SetStatus(UpdaterState.Installing);
            if (updateInstaller is null)
                throw new NotSupportedException(I18n.Strings.UpdateInstallationUnsupported);
            var request = new UpdateInstallRequest(DownloadPath, GithubAsset!.Digest!);
            await updateInstaller.StartAsync(request, token);
        }
        catch (OperationCanceledException)
        {
            SetDownloadedStatus();
        }
        catch (Exception ex)
        {
            await logger.WriteAsync(ex.ToString());
            SetErrorState(ex.Message);
        }
    }

    private readonly HashSet<string> notifiedVersion = [];
    private void SendNotification()
    {
        if (CurrentState.State is not (UpdaterState.ReadyForDownload
            or UpdaterState.UpdateAvailableAt3rdPartySrc
            or UpdaterState.UpdateAvailableAtMarket
            or UpdaterState.UpdateAvailable
            or UpdaterState.Downloaded
            or UpdaterState.ReadyToInstall))
        {
            return;
        }
        lock (installationGate)
        {
            if (string.IsNullOrEmpty(GithubRelease?.TagName) || !notifiedVersion.Add(GithubRelease.TagName))
                return;
        }

        var stateText = GetStateText(CurrentState.State);
        var readyToInstall = CurrentState.State == UpdaterState.ReadyToInstall;
        var button = readyToInstall
            ? new ActionButton(I18n.Strings.InstallUpdate, async () => await InstallUpdate(CancellationToken.None))
            : new ActionButton(I18n.Strings.GoToAboutPage, () => mainWindow.OpenPage(PageDefinition.About, null));
        var message = readyToInstall ? updateInfo.PackageName : I18n.Strings.CheckOnAboutPage;
        notificationManager.ShowText(stateText, message, [button]);
    }

    public Task RunAutoUpdateFlow(CancellationToken token = default)
    {
        return SingletonTask(runToken => AutoUpdateFlow(true, runToken))(token);
    }

    public Task RunUpdateFlow()
    {
        return SingletonTask(token => AutoUpdateFlow(false, token))(CancellationToken.None);
    }

    private async Task AutoUpdateFlow(bool isAutoUpdate, CancellationToken token)
    {
        /*if (updateInfo.ManageType == UpdateInfoConfig.TypeExternal)
        {
            return;
        }*/

        using var guard = new ScopeGuard(SendNotification);

        await CheckNewVersion(token);

        if (CurrentState.State != UpdaterState.ReadyForDownload)
        {
            return;
        }

        if (isAutoUpdate && configManager.GetConfig<ProgramConfig>().AutoDownloadUpdate is false)
        {
            return;
        }

        await DownloadUpdatePackage(token);
    }

    private async Task CheckNewVersion(CancellationToken token)
    {
        SetStatus(UpdaterState.CheckingForUpdate);
        (NeedUpdate, GithubRelease) = await githubUpdater.Check(token);
        if (NeedUpdate)
        {
            if (updateInfo.ManageType == UpdateInfoConfig.TypeMarket)
            {
                SetStatus(UpdaterState.UpdateAvailableAtMarket);
            }
            else if (updateInfo.ManageType == UpdateInfoConfig.TypeManual
                && updateInfo.UpdateSrc == "github"
                && !string.IsNullOrEmpty(updateInfo.PackageName))
            {
                GithubAsset = GetGithubAsset();
                if (GithubAsset != null)
                {
                    SetStatus(UpdaterState.ReadyForDownload);
                }
                else
                {
                    SetStatus(UpdaterState.UpdateAvailableAtGitHubExtra);
                }
            }
            else if (string.IsNullOrEmpty(updateInfo.UpdateSrc) is false && updateInfo.UpdateSrc != "github")
            {
                SetStatus(UpdaterState.UpdateAvailableAt3rdPartySrc);
            }
            else
            {
                SetStatus(UpdaterState.UpdateAvailable);
            }
        }
        else
        {
            SetStatus(UpdaterState.UpToDate);
        }
    }

    private async Task DownloadUpdatePackage(CancellationToken token)
    {
        DownloadProgress = new();
        SetStatus(UpdaterState.Downloading);
        if (GithubAsset == null)
        {
            throw new InvalidOperationException("No Update Available.");
        }

        var downloadUrl = GithubAsset.BrowserDownloadUrl!;
        Directory.CreateDirectory(Path.GetDirectoryName(DownloadPath)!);

        if (Directory.Exists(DownloadPath))
        {
            Directory.Delete(DownloadPath, true);
        }

        using var sha256 = SHA256.Create();
        if (File.Exists(DownloadPath))
        {
            var existFileStream = File.OpenRead(DownloadPath);
            var existHash = await sha256.ComputeHashAsync(existFileStream, token);
            var existHashString = $"sha256:{Convert.ToHexString(existHash)}";
            existFileStream.Dispose();
            if (string.Compare(GithubAsset.Digest, existHashString, StringComparison.OrdinalIgnoreCase) != 0)
            {
                File.Delete(DownloadPath);
            }
            else
            {
                SetDownloadedStatus();
                return;
            }
        }

        await http.GetFile(
            downloadUrl,
            DownloadPath,
            new Progress<HttpDownloadProgress>(progress =>
            {
                progress.TotalBytesToReceive ??= GithubAsset.Size;
                DownloadProgress = progress;
                DownloadProgressChanged?.Invoke(progress);
            }),
            token
        );

        using var fileStream = File.OpenRead(DownloadPath);
        var localHash = await sha256.ComputeHashAsync(fileStream, token);
        var localHashString = $"sha256:{Convert.ToHexString(localHash)}";

        if (string.Compare(GithubAsset.Digest, localHashString, StringComparison.OrdinalIgnoreCase) != 0)
        {
            logger.Write($"Downloaded file hash does not match the expected hash. Expected: {GithubAsset.Digest}, Actual: sha256:{BitConverter.ToString(localHash)}");
            throw new InvalidOperationException(I18n.Strings.HashMismatch);
        }

        SetDownloadedStatus();
    }

    private GitHubAsset? GetGithubAsset()
    {
        foreach (var asset in GithubRelease!.Assets ?? [])
        {
            if (asset.Name == updateInfo.PackageName)
            {
                return asset;
            }
        }

        return null;
    }

    private Task CancelUpdate(CancellationToken token)
    {
        singletonTask.Cancel();
        SetStatus(UpdaterState.Canceled);
        return Task.CompletedTask;
    }

    private Task OpenInFileManager(CancellationToken _)
    {
        Sys.ShowPathInFileManager(DownloadPath);
        return Task.CompletedTask;
    }

    private Task OpenUpdatePage(CancellationToken _)
    {
        Sys.OpenWithDefaultApp(GithubRelease!.HtmlUrl);
        return Task.CompletedTask;
    }

    private Task CopyPackageName(CancellationToken token)
    {
        return localClipboardSetter.Set(new TextProfile(updateInfo.PackageName), token);
    }

    private void SetErrorState(string message)
    {
        CurrentState = new UpdaterStatus(
            UpdaterState.Failed,
            message
        );
        StateChanged?.Invoke(CurrentState);
    }

    [MemberNotNull(nameof(CurrentState))]
    private void SetStatus(UpdaterState state)
    {
        var stateText = GetStateText(state);
        var (actionText, manualAction) = GetStateAction(state) ?? (string.Empty, null)!; //(CheckNewVersionText, SingletonTask(CheckNewVersion));
        CurrentState = new UpdaterStatus(state, stateText, actionText, manualAction);
        StateChanged?.Invoke(CurrentState);
    }

    private string GetStateText(UpdaterState state)
    {
        return state switch
        {
            UpdaterState.Idle => I18n.Strings.ReadyToCheckUpdate,
            UpdaterState.CheckingForUpdate => I18n.Strings.CheckingUpdate,
            UpdaterState.Canceled => I18n.Strings.Canceled,
            UpdaterState.ReadyForDownload => I18n.Strings.FoundNewVersion + GithubRelease!.TagName,
            UpdaterState.UpToDate => I18n.Strings.ItsLatestVersion,
            UpdaterState.UpdateAvailableAt3rdPartySrc => string.Format(I18n.Strings.UpdateFrom3rdSrc, GithubRelease!.TagName, updateInfo.UpdateSrc),
            UpdaterState.UpdateAvailableAtMarket => string.IsNullOrEmpty(updateInfo.UpdateSrc)
                ? I18n.Strings.FoundNewVersion + GithubRelease!.TagName
                : string.Format(I18n.Strings.UpdateFrom3rdSrc, GithubRelease!.TagName, updateInfo.UpdateSrc),
            UpdaterState.UpdateAvailable => I18n.Strings.FoundNewVersion + GithubRelease!.TagName,
            UpdaterState.Downloading => $"{I18n.Strings.Downloading} {updateInfo.PackageName}",
            UpdaterState.Downloaded => I18n.Strings.NewVersionDownloaded,
            UpdaterState.ReadyToInstall => I18n.Strings.UpdateReadyToInstall,
            UpdaterState.Installing => I18n.Strings.InstallingUpdate,
            UpdaterState.Failed => I18n.Strings.Error,
            UpdaterState.UpdateAvailableAtGitHubExtra => I18n.Strings.FoundNewVersion + GithubRelease!.TagName
                + string.Format(I18n.Strings.CurrentPackageDownloadManually, updateInfo.PackageName),
            _ => "Unknown state"
        };
    }

    private void SetDownloadedStatus()
        => SetStatus(updateInstaller is not null ? UpdaterState.ReadyToInstall : UpdaterState.Downloaded);

    private (string, CancelableTask)? GetStateAction(UpdaterState state)
    {
        return state switch
        {
            // UpdaterState.Idle => null,
            UpdaterState.CheckingForUpdate => (I18n.Strings.Cancel, SingletonTask(CancelUpdate)),
            // UpdaterState.Canceled => null,
            UpdaterState.ReadyForDownload => (I18n.Strings.DownloadInstaller, SingletonTask(DownloadUpdatePackage)),
            // UpdaterState.UpToDate => null,
            //UpdaterState.UpdateAvailableAt3rdPartySrc => null,
            UpdaterState.UpdateAvailableAtMarket when string.IsNullOrEmpty(updateInfo.PackageName) is false
                => (I18n.Strings.CopyPackageName, SingletonTask(CopyPackageName)),
            UpdaterState.UpdateAvailable => (I18n.Strings.OpenUpdatePage, OpenUpdatePage),
            UpdaterState.UpdateAvailableAtGitHubExtra => (I18n.Strings.OpenUpdatePage, OpenUpdatePage),
            UpdaterState.Downloading => (I18n.Strings.Cancel, SingletonTask(CancelUpdate)),
            UpdaterState.ReadyToInstall => (I18n.Strings.InstallUpdate, InstallUpdate),
            UpdaterState.Downloaded => (I18n.Strings.OpenFolder, SingletonTask(OpenInFileManager)),
            // UpdaterState.Failed => null,
            _ => null
        };
    }
}
