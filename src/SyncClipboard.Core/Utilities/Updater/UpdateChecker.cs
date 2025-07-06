using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities.Runner;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Models;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using static SyncClipboard.Core.Utilities.Updater.GitHubRelease;
using SyncClipboard.Abstract.Notification;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Core.Utilities.Updater;

using CancelableTask = Func<CancellationToken, Task>;

public class UpdateChecker : IStateMachine<UpdaterStatus>
{
    public UpdaterStatus CurrentState { get; private set; }
    public HttpDownloadProgress DownloadProgress { get; private set; }


    private bool NeedUpdate { get; set; } = false;
    private GitHubRelease? GithubRelease { get; set; } = null;
    private string DownloadPath
    {
        get
        {
            var versionFolder = Path.Combine(Env.UpdateFolder, GithubRelease?.TagName ?? "latest");
            if (!Directory.Exists(versionFolder))
            {
                Directory.CreateDirectory(versionFolder);
            }
            return Path.Combine(versionFolder, updateInfo.PackageName);
        }
    }

    private readonly GithubUpdater githubUpdater;
    private readonly IHttp http;
    private readonly ILogger logger;
    private readonly IMainWindow mainWindow;
    private readonly ConfigManager configManager;

    private readonly INotification notification;
    private readonly UpdateInfoConfig updateInfo;

    private readonly SingletonTask singletonTask = new();

    public event Action<UpdaterStatus>? StateChanged;
    public event Action<HttpDownloadProgress>? DownloadProgressChanged;

    public UpdateChecker(
        GithubUpdater githubUpdater,
        IHttp http,
        ILogger logger,
        INotification notification,
        IMainWindow mainWindow,
        ConfigManager configManager,
        [FromKeyedServices(Env.UpdateInfoFile)] ConfigBase updateInfoConfig)
    {
        this.githubUpdater = githubUpdater;
        this.http = http;
        this.logger = logger;
        this.configManager = configManager;
        this.notification = notification;
        this.mainWindow = mainWindow;
        updateInfo = updateInfoConfig.GetConfig<UpdateInfoConfig>();
        SetStatus(UpdaterState.Idle);
        logger.WriteAsync(updateInfo.ToString());
    }

    private CancelableTask SingletonTask(CancelableTask task)
    {
        return async token =>
        {
            try
            {
                await singletonTask.Run(task, token);
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

    private readonly HashSet<string> notifiedVersion = [];
    private void SendNotification()
    {
        if (CurrentState.State is not (UpdaterState.ReadyForDownload
            or UpdaterState.UpdateAvailableAt3rdPartySrc
            or UpdaterState.UpdateAvailable
            or UpdaterState.Downloaded))
        {
            return;
        }
        if (string.IsNullOrEmpty(GithubRelease?.TagName) || notifiedVersion.Add(GithubRelease.TagName) is false)
        {
            return;
        }

        var stateText = GetStateText(CurrentState.State);
        List<Button> buttons = [
            new Button(I18n.Strings.GoToAboutPage, () => mainWindow.OpenPage(PageDefinition.About, null))
        ];

        /*与特定state关联的按钮可能过时，需要在state改变时清除这个按钮

        var action = GetStateAction();
        if (action is not null)
        {
            var (actionText, manualAction) = action.Value;
            buttons.Add(new Button(actionText, () => manualAction(CancellationToken.None)));
        }*/

        notification.SendText(stateText, I18n.Strings.CheckOnAboutPage, buttons.ToArray());
    }

    public Task RunAutoUpdateFlow()
    {
        return SingletonTask(token => AutoUpdateFlow(true, token))(CancellationToken.None);
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

        var autoDownloadUpdate = configManager.GetConfig<ProgramConfig>().AutoDownloadUpdate;
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
            if (updateInfo.ManageType == UpdateInfoConfig.TypeManual
                && updateInfo.UpdateSrc == "github"
                && !string.IsNullOrEmpty(updateInfo.PackageName))
            {
                SetStatus(UpdaterState.ReadyForDownload);
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
        SetStatus(UpdaterState.Downloading);
        if (GithubRelease == null)
        {
            throw new InvalidOperationException("No Update Available.");
        }

        using var SHA256 = System.Security.Cryptography.SHA256.Create();
        var githubAsset = GetGithubAsset();
        var downloadUrl = githubAsset.BrowserDownloadUrl!;

        if (Directory.Exists(DownloadPath))
        {
            Directory.Delete(DownloadPath, true);
        }

        if (File.Exists(DownloadPath))
        {
            var existFileStream = File.OpenRead(DownloadPath);
            var existHash = await SHA256.ComputeHashAsync(existFileStream, token);
            var existHashString = $"sha256:{Convert.ToHexString(existHash)}";
            existFileStream.Dispose();
            if (string.Compare(githubAsset.Digest, existHashString, StringComparison.OrdinalIgnoreCase) != 0)
            {
                File.Delete(DownloadPath);
            }
            else
            {
                SetStatus(UpdaterState.Downloaded);
                return;
            }
        }

        await http.GetFile(
            downloadUrl,
            DownloadPath,
            new Progress<HttpDownloadProgress>(progress =>
            {
                progress.TotalBytesToReceive ??= githubAsset.Size;
                DownloadProgressChanged?.Invoke(progress);
            }),
            token
        );

        using var fileStream = File.OpenRead(DownloadPath);
        var localHash = await SHA256.ComputeHashAsync(fileStream, token);
        var localHashString = $"sha256:{Convert.ToHexString(localHash)}";

        if (string.Compare(githubAsset.Digest, localHashString, StringComparison.OrdinalIgnoreCase) != 0)
        {
            logger.Write($"Downloaded file hash does not match the expected hash. Expected: {githubAsset.Digest}, Actual: sha256:{BitConverter.ToString(localHash)}");
            throw new InvalidOperationException(I18n.Strings.HashMismatch);
        }

        SetStatus(UpdaterState.Downloaded);
    }

    private GitHubAsset GetGithubAsset()
    {
        foreach (var asset in GithubRelease!.Assets ?? [])
        {
            if (asset.Name == updateInfo.PackageName)
            {
                return asset;
            }
        }

        throw new InvalidOperationException($"No GitHub Asset for {updateInfo.PackageName} Available.");
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
            UpdaterState.UpdateAvailable => I18n.Strings.FoundNewVersion + GithubRelease!.TagName,
            UpdaterState.Downloading => $"{I18n.Strings.Downloading} {updateInfo.PackageName}",
            UpdaterState.Downloaded => I18n.Strings.NewVersionDownloaded,
            UpdaterState.Failed => I18n.Strings.Error,
            _ => "Unknown state"
        };
    }

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
            UpdaterState.UpdateAvailable => (I18n.Strings.OpenUpdatePage, OpenUpdatePage),
            UpdaterState.Downloading => (I18n.Strings.Cancel, SingletonTask(CancelUpdate)),
            UpdaterState.Downloaded => (I18n.Strings.OpenFolder, SingletonTask(OpenInFileManager)),
            // UpdaterState.Failed => null,
            _ => null
        };
    }
}