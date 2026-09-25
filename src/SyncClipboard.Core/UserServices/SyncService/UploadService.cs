using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using NativeNotification.Interface;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.UserServices.ClipboardService;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.Keyboard;
using SyncClipboard.Core.RemoteServer;

namespace SyncClipboard.Core.UserServices;

public class UploadService : ClipboardHander
{
    public event ProgramEvent.ProgramEventHandler? PushStarted;
    public event ProgramEvent.ProgramEventHandler? PushStopped;

    private static readonly string QuickUploadGuid = "D0EDB9A4-3409-4A76-BC2B-4C0CD80DD850";
    private static readonly string CopyAndQuickUploadGuid = "D13672E9-D14C-4D48-847E-10B030F4B608";
    private static readonly string QuickUploadWithoutFilterGuid = "6C5314DF-B504-25EA-074D-396E5C69BAF1";
    private static readonly string CopyAndQuickUploadWithoutFilterGuid = "40E0B462-FCED-C4CD-7126-1F5204443DC1";
    public UniqueCommand QuickUploadCommand => new UniqueCommand(
        I18n.Strings.UploadOnce,
        QuickUploadGuid,
        QuickUploadWithContentControl
    );
    public UniqueCommand CopyAndQuickUploadCommand => new UniqueCommand(
        I18n.Strings.CopyAndUpload,
        CopyAndQuickUploadGuid,
        CopyAndQuickUploadWithContentControl
    );
    public UniqueCommand QuickUploadWithoutFilterCommand => new UniqueCommand(
        I18n.Strings.UploadWithoutFilter,
        QuickUploadWithoutFilterGuid,
        QuickUploadIgnoreContentControl
    );
    public UniqueCommand CopyAndQuickUploadWithoutFilterCommand => new UniqueCommand(
        I18n.Strings.CopyAndUploadWithoutFilter,
        CopyAndQuickUploadWithoutFilterGuid,
        CopyAndQuickUploadIgnoreContentControl
    );

    private readonly static string SERVICE_NAME_SIMPLE = I18n.Strings.UploadService;
    public override string SERVICE_NAME => I18n.Strings.ClipboardSyncing;
    public override string LOG_TAG => "PUSH";

    protected override bool SwitchOn
    {
        get => _syncConfig.PushSwitchOn && _syncConfig.SyncSwitchOn && _remoteClipboardServerFactory.HasActiveServer;
        set
        {
            _syncConfig.SyncSwitchOn = value;
            _configManager.SetConfig(_syncConfig);
        }
    }

    private bool NotifyOnManualUpload => _syncConfig.NotifyOnManualUpload;
    private bool DoNotUploadWhenCut => _syncConfig.DoNotUploadWhenCut;

    private bool _downServiceChangingLocal = false;
    private Profile? _profileCache;
    private DownloadService DownloadService { get; set; } = null!;

    private readonly INotificationManager _notificationManager;
    private readonly ILogger _logger;
    private readonly ConfigManager _configManager;
    private readonly IClipboardFactory _clipboardFactory;
    private readonly LocalClipboardSetter _localClipboardSetter;
    private readonly IServiceProvider _serviceProvider;
    private readonly RemoteClipboardServerFactory _remoteClipboardServerFactory;
    private readonly ITrayIcon _trayIcon;
    private readonly IMessenger _messenger;
    private readonly VirtualKeyboard _keyboard;
    private readonly HotkeyManager _hotkeyManager;
    private readonly Lazy<ICurrentSelectedContentProvider?> _currentSelectedContentProvider;
    private SyncConfig _syncConfig;
    private ServerConfig _serverConfig;

    public UploadService(
        IServiceProvider serviceProvider,
        IMessenger messenger,
        VirtualKeyboard keyboard,
        HotkeyManager hotkeyManager,
        RemoteClipboardServerFactory remoteClipboardServerFactory)
    {
        _serviceProvider = serviceProvider;
        _logger = _serviceProvider.GetRequiredService<ILogger>();
        _configManager = _serviceProvider.GetRequiredService<ConfigManager>();
        _clipboardFactory = _serviceProvider.GetRequiredService<IClipboardFactory>();
        _localClipboardSetter = _serviceProvider.GetRequiredService<LocalClipboardSetter>();
        _notificationManager = _serviceProvider.GetRequiredService<INotificationManager>();
        _trayIcon = _serviceProvider.GetRequiredService<ITrayIcon>();
        _messenger = messenger;
        _syncConfig = _configManager.GetConfig<SyncConfig>();
        _serverConfig = _configManager.GetConfig<ServerConfig>();
        _keyboard = keyboard;
        _hotkeyManager = hotkeyManager;
        _currentSelectedContentProvider = new(
            () => _serviceProvider.GetService<ICurrentSelectedContentProvider>());
        _remoteClipboardServerFactory = remoteClipboardServerFactory;

        ContextMenuGroupName = SyncService.ContextMenuGroupName;
    }

    public override void Load()
    {
        _syncConfig = _configManager.GetConfig<SyncConfig>();
        _serverConfig = _configManager.GetConfig<ServerConfig>();
        if (!SwitchOn)
        {
            _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, "Stopped.");
        }
        else
        {
            _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, "Running.");
        }
        base.Load();
    }

    protected override void StartService()
    {
        _remoteClipboardServerFactory.CurrentServerChanged += OnCurrentServerChanged;
        DownloadService = _serviceProvider.GetRequiredService<DownloadService>();
        base.StartService();
    }

    protected override void StopSerivce()
    {
        _remoteClipboardServerFactory.CurrentServerChanged -= OnCurrentServerChanged;
        _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, "Stopped.");
        base.StopSerivce();
    }

    private void OnCurrentServerChanged(object? sender, EventArgs e)
    {
        CancelProcess();
        Load();
    }

    public override void RegistEvent()
    {
        var pushStartedEvent = new ProgramEvent(
            (handler) => PushStarted += handler,
            (handler) => PushStarted -= handler
        );
        Event.RegistEvent(SyncService.PUSH_START_ENENT_NAME, pushStartedEvent);

        var pushStoppedEvent = new ProgramEvent(
            (handler) => PushStopped += handler,
            (handler) => PushStopped -= handler
        );
        Event.RegistEvent(SyncService.PUSH_STOP_ENENT_NAME, pushStoppedEvent);
    }

    public override void RegistEventHandler()
    {
        _messenger.Register<EmptyMessage, string>(this, SyncService.PULL_START_ENENT_NAME, PullStartedHandler);
        _messenger.Register<Profile, string>(this, SyncService.PULL_STOP_ENENT_NAME, PullStoppedHandler);
        base.RegistEventHandler();
    }

    public override void UnRegistEventHandler()
    {
        _messenger.UnregisterAll(this);
        base.UnRegistEventHandler();
    }

    public void PullStartedHandler(object _, EmptyMessage _1)
    {
        _logger.Write("_isChangingLocal set to TRUE");
        _downServiceChangingLocal = true;
    }

    public void PullStoppedHandler(object _, Profile profile)
    {
        _logger.Write("_isChangingLocal set to FALSE");
        _profileCache = profile;
        _downServiceChangingLocal = false;
    }

    private void SetWorkingStartStatus()
    {
        _trayIcon.ShowUploadAnimation();
        _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, "Uploading.");
    }

    private void SetWorkingEndStatus()
    {
        _trayIcon.StopAnimation();
    }

    private async Task<bool> IsDownloadServiceWorking(Profile profile, CancellationToken token)
    {
        if (await Profile.Same(profile, _profileCache, token))
        {
            await _logger.WriteAsync(LOG_TAG, "Same as lasted downloaded profile, won't push.");
            _profileCache = null;
            return true;
        }

        return _downServiceChangingLocal;
    }

    private async Task<bool> IsObsoleteProfile(Profile profile, CancellationToken token)
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }
        try
        {
            var latest = await _clipboardFactory.CreateProfileFromLocal(token);
            if (latest is GroupProfile { ContainsRootDirectory: true })
            {
                return true;
            }
            if (await Profile.Same(profile, latest, token))
            {
                return false;
            }
            return true;
        }
        catch when (token.IsCancellationRequested is false)
        {
            return false;
        }
    }

    private async Task<string?> GetContentControlSkipReasonAsync(ClipboardMetaInfomation meta, Profile profile, CancellationToken token)
    {
        if (DoNotUploadWhenCut && (meta.Effects & DragDropEffects.Move) == DragDropEffects.Move)
        {
            return "Skipped: Cutting operation detected.";
        }

        if (!_syncConfig.IgnoreExcludeForSyncSuggestion && (meta.ExcludeForSync ?? false))
        {
            return "Skipped: Sensitive content marked by system.";
        }

        return await ContentControlHelper.IsContentValid(profile, token);
    }

    protected override async Task HandleClipboard(ClipboardMetaInfomation meta, Profile profile, CancellationToken token)
    {
        var filterConfig = _configManager.GetConfig<ClipboardOwnerFilterConfig>();
        if (ClipboardOwnerFilterHelper.ShouldFilter(filterConfig, meta.Owner))
        {
            _logger.Write(LOG_TAG, "Stop Push: Filtered by clipboard owner.");
            _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, "Skipped: Filtered by clipboard owner.", false);
            return;
        }
        var result = await CheckAndUpload(meta, profile, true, token, notifyFailure: true);
        if (!result.Success && profile is GroupProfile { ContainsRootDirectory: true })
        {
            _notificationManager.SharedQuickMessage(SERVICE_NAME_SIMPLE, result.Reason ?? string.Empty);
        }
    }

    private async Task<UploadResult> SkipUpload(string reason)
    {
        await _logger.WriteAsync(LOG_TAG, reason);
        _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, reason, false);
        return new(false, reason);
    }

    protected async Task<UploadResult> CheckAndUpload(
        ClipboardMetaInfomation meta, Profile profile, bool contentControl, CancellationToken token, bool notifyFailure = false)
    {
        await _logger.WriteAsync(LOG_TAG, "New Push started, meta: " + meta);
        UploadResult result;

        try
        {
            token.ThrowIfCancellationRequested();
            if (profile is GroupProfile { ContainsRootDirectory: true })
            {
                return await SkipUpload(I18n.Strings.RootDirectoryNotSupported);
            }

            if (await IsDownloadServiceWorking(profile, token))
            {
                return await SkipUpload("Skipped: Download service is working or clipboard matches the last downloaded content.");
            }
            if (await IsObsoleteProfile(profile, token))
            {
                return await SkipUpload("Skipped: Clipboard content has changed.");
            }
            if (contentControl)
            {
                var reason = await GetContentControlSkipReasonAsync(meta, profile, token);
                if (reason is not null)
                {
                    return await SkipUpload(reason);
                }
            }

            if (profile.Type == ProfileType.Unknown)
            {
                return await SkipUpload("Skipped: Clipboard content type is not supported.");
            }

            result = await UploadClipboard(profile, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            await _logger.WriteAsync("Upload", "Upload Canceled");
            throw;
        }
        catch (Exception ex)
        {
            await _logger.WriteAsync(LOG_TAG, $"Upload failed: {ex.Message}\n{ex.StackTrace}");
            _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, I18n.Strings.FailedToUpload + ex.Message, true);
            result = new(false, ex.Message);
        }
        finally
        {
            await _logger.WriteAsync(LOG_TAG, "Push End");
        }

        if (notifyFailure && !result.Success)
        {
            _notificationManager.ShowText(I18n.Strings.FailedToUpload + profile.ShortDisplayText, result.Reason ?? string.Empty);
        }
        return result;
    }

    private async Task<UploadResult> UploadClipboard(Profile currentProfile, CancellationToken token)
    {
        PushStarted?.Invoke();
        using var eventGuard = new ScopeGuard(() => PushStopped?.Invoke());

        SetWorkingStartStatus();
        using var workingStatusGuard = new ScopeGuard(SetWorkingEndStatus);

        await SyncService.remoteProfilemutex.WaitAsync(token);
        using var mutexGuard = new ScopeGuard(() => SyncService.remoteProfilemutex.Release());

        var result = await UploadLoop(currentProfile, token);
        if (result.Success)
        {
            DownloadService.SetRemoteCache(currentProfile);
            _profileCache = currentProfile;
        }
        return result;
    }

    private async Task<UploadResult> UploadLoop(Profile profile, CancellationToken cancelToken)
    {
        string errMessage = "";
        string? stackTrace = null;
        for (int i = 0; i <= _syncConfig.RetryTimes; i++)
        {
            ProgressToastReporter? toastReporter = null;
            try
            {
                cancelToken.ThrowIfCancellationRequested();
                var remoteServer = _remoteClipboardServerFactory.Current;
                var remoteProfile = await remoteServer.GetProfileAsync(cancelToken) ?? new UnknownProfile();

                if (!await Profile.Same(remoteProfile, profile, cancelToken))
                {
                    await _logger.WriteAsync(LOG_TAG, "Start: " + profile.DisplayText);
                    if (profile.HasTransferData)
                    {
                        toastReporter = new ProgressToastReporter(
                            SERVICE_NAME_SIMPLE,
                            profile.ShortDisplayText,
                            I18n.Strings.UploadingFile,
                            useToast: _syncConfig.NotifyFileSyncProgress);
                    }

                    await remoteServer.SetProfileAsync(profile, toastReporter, cancelToken);
                }
                else
                {
                    await _logger.WriteAsync(LOG_TAG, "Remote is same as local, won't push.");
                }
                cancelToken.ThrowIfCancellationRequested();
                _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, "Running.", false);
                return new(true);
            }
            catch (OperationCanceledException) when (cancelToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TaskCanceledException)
            {
                cancelToken.ThrowIfCancellationRequested();
                _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, string.Format(I18n.Strings.UploadFailedStatusTimeout, i + 1), true);
                errMessage = I18n.Strings.Timeout;
            }
            catch (Exception ex)
            {
                errMessage = ex.Message;
                stackTrace = ex.StackTrace;
                _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, string.Format(I18n.Strings.UploadFailedStatus, i + 1, errMessage), true);
            }
            finally
            {
                toastReporter?.CancelSicent();
            }

            if (i < _syncConfig.RetryTimes)
            {
                await Task.Delay(TimeSpan.FromSeconds(_syncConfig.IntervalTime), cancelToken);
            }
        }
        var status = profile.ShortDisplayText;
        _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, $"{I18n.Strings.FailedToUpload}{status[..Math.Min(status.Length, 200)]}\n{errMessage}", true);
        await _logger.WriteAsync(LOG_TAG, $"Upload failed after {_syncConfig.RetryTimes + 1} times, last error: {errMessage}\n{stackTrace}");
        return new(false, errMessage);
    }

    private async void QuickUpload(bool contentControl) => await QuickUploadAsync(contentControl, null, null);

    private async Task QuickUploadAsync(bool contentControl, ClipboardMetaInfomation? meta, Profile? profile)
    {
        if (!_remoteClipboardServerFactory.HasActiveServer)
        {
            _notificationManager.ShowText("SyncClipboard", I18n.Strings.SyncAccountNotSelected);
            return;
        }

        var token = StopPreviousAndGetNewToken();
        try
        {
            meta ??= await _clipboardFactory.GetMetaInfomation(token);
            profile ??= await _clipboardFactory.CreateProfileFromMeta(meta, contentControl, token);
            _profileCache = null;
            var result = await CheckAndUpload(meta, profile, contentControl, token);
            ShowManualUploadResult(result, profile.ShortDisplayText);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            ShowManualUploadFailure(ex);
        }
    }

    private void QuickUploadWithContentControl() => QuickUpload(true);
    private void QuickUploadIgnoreContentControl() => QuickUpload(false);

    private async void CopyAndQuickUpload(bool contentControl, string cmdId)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var token = timeoutCts.Token;

            var selectedContent = await Task.Run(
                () => _currentSelectedContentProvider.Value?.GetCurrentSelectedContent(),
                token).WaitAsync(token);

            Profile? selectedProfile = null;
            if (selectedContent is not null)
            {
                selectedProfile = await SetContentToClipboard(selectedContent, contentControl, token);
                await _logger.WriteAsync(LOG_TAG, "Using content read directly from the current selection.");
            }
            else
            {
                await _logger.WriteAsync(LOG_TAG, "Current selection could not be read directly; falling back to the copy shortcut.");
                await Task.Run(() =>
                {
                    ReleaseHotkeyKeys(cmdId);
                    _keyboard.Copy();
                }, token).WaitAsync(token);
            }

            await Task.Delay(200);
            await QuickUploadAsync(contentControl, selectedContent, selectedProfile);
        }
        catch (Exception ex)
        {
            ShowManualUploadFailure(ex);
        }
    }

    private async Task<Profile> SetContentToClipboard(ClipboardMetaInfomation selectedContent, bool contentControl, CancellationToken token)
    {
        var profile = await _clipboardFactory.CreateProfileFromMeta(selectedContent, contentControl, token);
        await _localClipboardSetter.Set(profile, token);
        return profile;
    }

    private void ShowManualUploadFailure(Exception ex)
        => ShowManualUploadResult(new(false, ex.Message));

    private void ShowManualUploadResult(UploadResult result, string displayText = "")
    {
        if (!NotifyOnManualUpload)
        {
            return;
        }

        var notification = _notificationManager.Shared;
        notification.Title = result.Success ? I18n.Strings.Uploaded : I18n.Strings.ManualUploadFailed;
        notification.Message = result.Success ? displayText : result.Reason ?? string.Empty;
        notification.Show(new NotificationDeliverOption { Duration = TimeSpan.FromSeconds(2) });
    }

    private void ReleaseHotkeyKeys(string cmdId)
    {
        if (_hotkeyManager.HotkeyStatusMap.TryGetValue(cmdId, out var status))
        {
            if (status.Hotkey is not null) _keyboard.ReleaseKeys(status.Hotkey);
        }
    }

    private void CopyAndQuickUploadWithContentControl() => CopyAndQuickUpload(true, CopyAndQuickUploadGuid);
    private void CopyAndQuickUploadIgnoreContentControl() => CopyAndQuickUpload(false, CopyAndQuickUploadWithoutFilterGuid);
}
