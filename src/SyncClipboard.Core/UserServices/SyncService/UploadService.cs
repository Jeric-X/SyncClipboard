using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using NativeNotification.Interface;
using SharpHook;
using SharpHook.Native;
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
        get => _syncConfig.PushSwitchOn && _syncConfig.SyncSwitchOn;
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
    private readonly IServiceProvider _serviceProvider;
    private readonly RemoteClipboardServerFactory _remoteClipboardServerFactory;
    private readonly ITrayIcon _trayIcon;
    private readonly IMessenger _messenger;
    private readonly IEventSimulator _keyEventSimulator;
    private readonly HotkeyManager _hotkeyManager;
    private SyncConfig _syncConfig;
    private ServerConfig _serverConfig;

    public UploadService(
        IServiceProvider serviceProvider,
        IMessenger messenger,
        IEventSimulator keyEventSimulator,
        HotkeyManager hotkeyManager,
        RemoteClipboardServerFactory remoteClipboardServerFactory)
    {
        _serviceProvider = serviceProvider;
        _logger = _serviceProvider.GetRequiredService<ILogger>();
        _configManager = _serviceProvider.GetRequiredService<ConfigManager>();
        _clipboardFactory = _serviceProvider.GetRequiredService<IClipboardFactory>();
        _notificationManager = _serviceProvider.GetRequiredService<INotificationManager>();
        _trayIcon = _serviceProvider.GetRequiredService<ITrayIcon>();
        _messenger = messenger;
        _syncConfig = _configManager.GetConfig<SyncConfig>();
        _serverConfig = _configManager.GetConfig<ServerConfig>();
        _keyEventSimulator = keyEventSimulator;
        _hotkeyManager = hotkeyManager;
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
        DownloadService = _serviceProvider.GetRequiredService<DownloadService>();
        base.StartService();
    }

    protected override void StopSerivce()
    {
        _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, "Stopped.");
        base.StopSerivce();
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

    private async Task<bool> ValidateContentControlAsync(ClipboardMetaInfomation meta, Profile profile, CancellationToken token)
    {
        if (DoNotUploadWhenCut && (meta.Effects & DragDropEffects.Move) == DragDropEffects.Move)
        {
            await _logger.WriteAsync(LOG_TAG, "Cut won't Push.");
            _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, "Skipped: Cutting operation detected.", false);
            return false;
        }

        if (!_syncConfig.IgnoreExcludeForSyncSuggestion && (meta.ExcludeForSync ?? false))
        {
            await _logger.WriteAsync(LOG_TAG, "Stop Push for meta exclude for sync.");
            _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, "Skipped: Sensitive content marked by system.", false);
            return false;
        }

        var skipReason = await ContentControlHelper.IsContentValid(profile, token);
        if (skipReason != null)
        {
            await _logger.WriteAsync(LOG_TAG, "Stop Push: " + skipReason);
            _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, skipReason, false);
            return false;
        }

        return true;
    }

    protected override Task HandleClipboard(ClipboardMetaInfomation meta, Profile profile, CancellationToken token)
    {
        return UploadProfileClipboard(meta, profile, true, token);
    }

    protected async Task UploadProfileClipboard(ClipboardMetaInfomation meta, Profile profile, bool contentControl, CancellationToken token)
    {
        await _logger.WriteAsync(LOG_TAG, "New Push started, meta: " + meta);

        using var endLogGuard = new ScopeGuard(() => _logger.WriteAsync(LOG_TAG, "Push End").GetAwaiter().GetResult());

        await SyncService.remoteProfilemutex.WaitAsync(token);
        try
        {
            if (await IsDownloadServiceWorking(profile, token))
            {
                await _logger.WriteAsync(LOG_TAG, "Stop Push: Download service is working or profile is same as last downloaded.");
                _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, "Running.", false);
                return;
            }
            if (await IsObsoleteProfile(profile, token))
            {
                await _logger.WriteAsync(LOG_TAG, "Stop Push: Clipboard profile is obsolete.");
                _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, "Running.", false);
                return;
            }
            if (contentControl && !await ValidateContentControlAsync(meta, profile, token))
            {
                return;
            }

            SetWorkingStartStatus();
            using var workingStatusGuard = new ScopeGuard(SetWorkingEndStatus);
            await UploadClipboard(profile, token);
            DownloadService.SetRemoteCache(profile);
            _profileCache = profile;
        }
        catch (OperationCanceledException)
        {
            await _logger.WriteAsync("Upload", "Upload Canceled");
        }
        finally
        {
            SyncService.remoteProfilemutex.Release();
        }
    }

    private async Task UploadClipboard(Profile currentProfile, CancellationToken cancelToken)
    {
        if (currentProfile.Type == ProfileType.Unknown)
        {
            await _logger.WriteAsync("Local profile type is Unkown, stop upload.");
            _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, "Local profile type is unkown, stopped.", false);
            return;
        }

        PushStarted?.Invoke();
        using var eventGuard = new ScopeGuard(() => PushStopped?.Invoke());

        await UploadLoop(currentProfile, cancelToken);
    }

    private async Task UploadLoop(Profile profile, CancellationToken cancelToken)
    {
        string errMessage = "";
        string? stackTrace = null;
        for (int i = 0; i <= _syncConfig.RetryTimes; i++)
        {
            ProgressToastReporter? toastReporter = null;
            try
            {
                var remoteServer = _remoteClipboardServerFactory.Current;
                var remoteProfile = await remoteServer.GetProfileAsync(cancelToken) ?? new UnknownProfile();

                if (!await Profile.Same(remoteProfile, profile, cancelToken))
                {
                    await _logger.WriteAsync(LOG_TAG, "Start: " + profile.DisplayText);
                    if (profile.HasTransferData)
                    {
                        toastReporter = ProgressToastReporter.CreateWithTrayProgress(
                            profile.ShortDisplayText,
                            I18n.Strings.UploadingFile,
                            SERVICE_NAME_SIMPLE,
                            "Uploading");
                    }

                    await remoteServer.SetProfileAsync(profile, toastReporter, cancelToken);
                }
                else
                {
                    await _logger.WriteAsync(LOG_TAG, "Remote is same as local, won't push.");
                }
                _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, "Running.", false);
                return;
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

            await Task.Delay(TimeSpan.FromSeconds(_syncConfig.IntervalTime), cancelToken);
        }
        var status = profile.ShortDisplayText;
        _notificationManager.ShowText(I18n.Strings.FailedToUpload + status, errMessage);
        _trayIcon.SetStatusString(SERVICE_NAME_SIMPLE, $"{I18n.Strings.FailedToUpload}{status[..Math.Min(status.Length, 200)]}\n{errMessage}", true);
        await _logger.WriteAsync(LOG_TAG, $"Upload failed after {_syncConfig.RetryTimes + 1} times, last error: {errMessage}\n{stackTrace}");
    }

    private async void QuickUpload(bool contentControl)
    {
        var token = StopPreviousAndGetNewToken();
        try
        {
            var meta = await _clipboardFactory.GetMetaInfomation(token);
            var profile = await _clipboardFactory.CreateProfileFromMeta(meta, contentControl, token);
            await UploadProfileClipboard(meta, profile, contentControl, token);
            if (NotifyOnManualUpload)
            {
                var notification = _notificationManager.Shared;
                notification.Title = I18n.Strings.Uploaded;
                notification.Message = profile.ShortDisplayText;
                notification.Show(new NotificationDeliverOption { Duration = TimeSpan.FromSeconds(2) });
            }
        }
        catch (Exception ex)
        {
            if (NotifyOnManualUpload)
            {
                var notification = _notificationManager.Shared;
                notification.Title = "Failed to upload manually";
                notification.Message = ex.Message;
                notification.Show(new NotificationDeliverOption { Duration = TimeSpan.FromSeconds(2) });
            }
        }
    }

    private void QuickUploadWithContentControl() => QuickUpload(true);
    private void QuickUploadIgnoreContentControl() => QuickUpload(false);

    private async void CopyAndQuickUpload(bool contentControl, string cmdId)
    {
        await Task.Run(() =>
        {
            if (_hotkeyManager.HotkeyStatusMap.TryGetValue(cmdId, out var status))
            {
                status.Hotkey?.Keys.ForEach(key => _keyEventSimulator.SimulateKeyRelease(KeyCodeMap.MapReverse[key]));
            }

            KeyCode modifier = OperatingSystem.IsMacOS() ? KeyCode.VcLeftMeta : KeyCode.VcLeftControl;

            _keyEventSimulator.SimulateKeyPress(modifier);
            _keyEventSimulator.SimulateKeyPress(KeyCode.VcC);

            _keyEventSimulator.SimulateKeyRelease(KeyCode.VcC);
            _keyEventSimulator.SimulateKeyRelease(modifier);
        });
        await Task.Delay(200);
        QuickUpload(contentControl);
    }

    private void CopyAndQuickUploadWithContentControl() => CopyAndQuickUpload(true, CopyAndQuickUploadGuid);
    private void CopyAndQuickUploadIgnoreContentControl() => CopyAndQuickUpload(false, CopyAndQuickUploadWithoutFilterGuid);
}