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

namespace SyncClipboard.Core.Utilities.Updater;

using CancelableTask = Func<CancellationToken, Task>;

public class UpdateChecker : IStateMachine<UpdaterStatus>
{
    public UpdaterStatus CurrentState { get; private set; }
    public HttpDownloadProgress DownloadProgress { get; private set; }


    private bool NeedUpdate { get; set; } = false;
    private GitHubRelease? GithubRelease { get; set; } = null;
    private string DownloadPath => Path.Combine(Env.UpdateFolder, updateInfo.PackageName);


    private readonly GithubUpdater githubUpdater;
    private readonly IHttp http;
    private readonly ILogger logger;
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
        ConfigManager configManager,
        [FromKeyedServices(Env.UpdateInfoFile)] ConfigBase updateInfoConfig)
    {
        this.githubUpdater = githubUpdater;
        this.http = http;
        this.logger = logger;
        this.configManager = configManager;
        this.notification = notification;
        updateInfo = updateInfoConfig.GetConfig<UpdateInfoConfig>();
        SetStatus(UpdaterState.Idle);
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

    private void SendNotification()
    {
        if (CurrentState.State is not (UpdaterState.ReadyForDownload
            or UpdaterState.UpdateAvailableAt3rdPartySrc
            or UpdaterState.UpdateAvailable
            or UpdaterState.Downloaded))
        {
            return;
        }

        var stateText = GetStateText(CurrentState.State);
        List<Button> buttons = [
            new Button("Open About Page", () => { })
        ];

        // 与特定state关联的按钮可能过时，需要在state改变时清除之前的通知
        // 
        // var action = GetStateAction();
        // if (action is not null)
        // {
        //     var (actionText, manualAction) = action.Value;
        //     buttons.Add(new Button(actionText, () => manualAction(CancellationToken.None)));
        // }

        notification.SendText(stateText, "Please check in About page.", buttons.ToArray());
    }

    public Task RunUpdateFlow()
    {
        return SingletonTask(UpdateFlow)(CancellationToken.None);
    }

    private async Task UpdateFlow(CancellationToken token)
    {
        if (updateInfo.ManageType == UpdateInfoConfig.TypeExternal)
        {
            return;
        }

        using var guard = new ScopeGuard(SendNotification);

        await CheckNewVersion(token);

        if (CurrentState.State != UpdaterState.ReadyForDownload)
        {
            return;
        }

        if (configManager.GetConfig<ProgramConfig>().AutoDownloadUpdate is false)
        {
            return;
        }

        await DownloadUpdatePackage(token);
    }

    private const string CheckNewVersionText = "Check for Updates";
    private async Task CheckNewVersion(CancellationToken token)
    {
        SetStatus(UpdaterState.CheckingForUpdate);
        (NeedUpdate, GithubRelease) = await githubUpdater.Check(token);
        if (NeedUpdate)
        {
            if (updateInfo.ManageType == UpdateInfoConfig.TypeManual && updateInfo.UpdateSrc == "github")
            {
                SetStatus(UpdaterState.ReadyForDownload);
            }
            else if (string.IsNullOrEmpty(updateInfo.UpdateSrc) is false)
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

    private const string DownloadUpdateText = "Download Update Package";
    private async Task DownloadUpdatePackage(CancellationToken token)
    {
        SetStatus(UpdaterState.Downloading);
        if (GithubRelease == null)
        {
            throw new InvalidOperationException("No Update Available.");
        }

        var githubAsset = GetGithubAsset();
        var downloadUrl = githubAsset.BrowserDownloadUrl!;
        await http.GetFile(
            downloadUrl,
            DownloadPath,
            new Progress<HttpDownloadProgress>(progress =>
            {
                DownloadProgressChanged?.Invoke(progress);
            }),
            token
        );

        using var SHA256 = System.Security.Cryptography.SHA256.Create();
        using var fileStream = File.OpenRead(DownloadPath);
        var localHash = await SHA256.ComputeHashAsync(fileStream, token);

        if (string.Compare(githubAsset.Digest, $"sha256:{BitConverter.ToString(localHash)}", StringComparison.OrdinalIgnoreCase) != 0)
        {
            logger.Write($"Downloaded file hash does not match the expected hash. Expected: {githubAsset.Digest}, Actual: sha256:{BitConverter.ToString(localHash)}");
            throw new InvalidOperationException("Downloaded file hash does not match the expected hash.");
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

    private const string CancelText = "Cancel";
    private Task CancelUpdate(CancellationToken token)
    {
        singletonTask.Cancel();
        SetStatus(UpdaterState.Canceled);
        return Task.CompletedTask;
    }

    private const string OpenInFileManagerText = "Open in File Manager";
    private Task OpenInFileManager(CancellationToken _)
    {
        SetStatus(UpdaterState.Canceled);
        Sys.ShowPathInFileManager(DownloadPath);
        return Task.CompletedTask;
    }

    private void SetErrorState(string message)
    {
        CurrentState = new UpdaterStatus(
            UpdaterState.Failed,
            $"Checking for update failed with message: {message}",
            CheckNewVersionText,
            SingletonTask(CheckNewVersion)
        );
        StateChanged?.Invoke(CurrentState);
    }

    [MemberNotNull(nameof(CurrentState))]
    private void SetStatus(UpdaterState state)
    {
        var stateText = GetStateText(state);
        var res = GetStateAction();
        var (actionText, manualAction) = GetStateAction() ?? (CheckNewVersionText, SingletonTask(CheckNewVersion));
        CurrentState = new UpdaterStatus(state, stateText, actionText, manualAction);
        StateChanged?.Invoke(CurrentState);
    }

    private string GetStateText(UpdaterState state)
    {
        return state switch
        {
            UpdaterState.Idle => "Ready for checking updates.",
            UpdaterState.CheckingForUpdate => "Checking for updates...",
            UpdaterState.Canceled => "Update check canceled.",
            UpdaterState.ReadyForDownload => $"New version {GithubRelease!.TagName} found.",
            UpdaterState.UpToDate => "You are using the latest version.",
            UpdaterState.UpdateAvailableAt3rdPartySrc => $"New version {GithubRelease!.TagName} found, please update from {updateInfo.UpdateSrc}.",
            UpdaterState.UpdateAvailable => $"New version {GithubRelease!.TagName} found.",
            UpdaterState.Downloading => $"Downloading {updateInfo.PackageName}...",
            UpdaterState.Downloaded => $"New version {GithubRelease!.TagName} package has been downloaded.",
            UpdaterState.Failed => "Update check failed.",
            _ => "Unknown state"
        };
    }

    private (string, CancelableTask)? GetStateAction()
    {
        return CurrentState.State switch
        {
            // UpdaterState.Idle => null,
            UpdaterState.CheckingForUpdate => (CancelText, SingletonTask(CancelUpdate)),
            // UpdaterState.Canceled => null,
            UpdaterState.ReadyForDownload => (DownloadUpdateText, SingletonTask(DownloadUpdatePackage)),
            // UpdaterState.UpToDate => null,
            UpdaterState.UpdateAvailableAt3rdPartySrc => null,
            // UpdaterState.UpdateAvailable => null,
            UpdaterState.Downloading => (CancelText, SingletonTask(CancelUpdate)),
            UpdaterState.Downloaded => (OpenInFileManagerText, SingletonTask(OpenInFileManager)),
            // UpdaterState.Failed => null,
            _ => null
        };
    }
}