using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Permissions;

namespace SyncClipboard.Core.Utilities.Permissions;

/*
 * AX = AXIsProcessTrusted，CG = CGPreflightPostEventAccess
 * macOS 26.7.1 原生接口实测：CG 首次返回 false 后，即使用户授权，本次运行后续 CG 检查仍返回 false，必须重启应用才能生效；
 * 但是如果只检查 AX 发现失败，此时提示用户授予权限后再检查 CG，可以得到正常的已授权状态，此时无需重启。
 *
 * 任何情况优先检查 AX 再检查 CG，尽量在 AX 阶段解决权限问题，防止进程缓存 CG 结果。
 * 如果 CG 检查明确返回 false，申请权限时应提醒用户：完成授权后需要重启应用。
 */
public sealed class MacOSPermissionProvider : IPermissionProvider
{
    private readonly ILogger _logger;
    private readonly IThreadDispatcher _dispatcher;
    private readonly IGlobalDialog _dialog;
    private readonly Action _openAccessibilitySettings;
    private readonly Action _requestAccessibilityPermission;
    private readonly Func<Task> _resetAccessibilityPermission;
    private readonly Func<bool> _isAccessibilityEnabled;
    private readonly Func<bool> _canPostEvents;
    private int _requestInProgress;

    public MacOSPermissionProvider(ILogger logger, IThreadDispatcher dispatcher, IGlobalDialog dialog)
        : this(logger, dispatcher, dialog, MacOSAccessibility.OpenSettings, MacOSAccessibility.Request,
            MacOSAccessibility.ResetAsync, MacOSAccessibility.AXIsProcessTrusted, MacOSAccessibility.CGPreflightPostEventAccess)
    { }

    internal MacOSPermissionProvider(ILogger logger, IThreadDispatcher dispatcher, IGlobalDialog dialog,
        Action openAccessibilitySettings, Action requestAccessibilityPermission, Func<Task> resetAccessibilityPermission,
        Func<bool> isAccessibilityEnabled, Func<bool> canPostEvents)
    {
        _logger = logger;
        _dispatcher = dispatcher;
        _dialog = dialog;
        _openAccessibilitySettings = openAccessibilitySettings;
        _requestAccessibilityPermission = requestAccessibilityPermission;
        _resetAccessibilityPermission = resetAccessibilityPermission;
        _isAccessibilityEnabled = isAccessibilityEnabled;
        _canPostEvents = canPostEvents;
    }

    public PermissionState GetState(PermissionCapability capability) => capability switch
    {
        PermissionCapability.SimulateInput => GetAccessibilityState(),
        PermissionCapability.ReadWindow or PermissionCapability.ReadCaret => GetPermissionState(_isAccessibilityEnabled),
        // macOS uses Carbon hotkeys rather than a global keyboard hook.
        PermissionCapability.MonitorKeyboard => PermissionState.NotRequired,
        _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, null)
    };

    private PermissionState GetAccessibilityState()
    {
        var accessibility = GetPermissionState(_isAccessibilityEnabled);
        if (accessibility != PermissionState.Available)
            return accessibility;
        var postEvents = GetPermissionState(_canPostEvents);
        return postEvents == PermissionState.Denied ? PermissionState.Partial : postEvents;
    }

    public IReadOnlyList<PermissionItem> GetPermissions()
    {
        var state = GetAccessibilityState();
        PermissionAction action = state == PermissionState.Available
            ? new(Strings.OpenSystemSettings, OpenSettingsAsync)
            : new(Strings.RequestPermission, () => RequestAccessibilityPermissionAsync(GetAccessibilityState));
        return [new(Strings.AccessibilityPermission, Strings.AccessibilityPermissionDescription, state, [action])];
    }

    public bool CheckAndRequest(PermissionCapability capability)
    {
        if (GetState(capability).IsGranted())
            return true;
        DelegateExtention.SafeFireAndForget(() => RequestAccessibilityPermissionAsync(() => GetState(capability)),
            nameof(MacOSPermissionProvider));
        return false;
    }

    private async Task RequestAccessibilityPermissionAsync(Func<PermissionState> getState)
    {
        if (Interlocked.CompareExchange(ref _requestInProgress, 1, 0) != 0)
            return;
        try
        {
            await _dispatcher.RunOnMainThreadAsync(async () =>
            {
                var state = getState();
                if (state.IsGranted())
                    return;
                var message = Strings.AccessibilityPermissionRequestMessage;
                // Partial 表示 AX 检查通过，但 CG 检查明确未通过，必须提示用户重启应用生效。
                if (state == PermissionState.Partial)
                    message += $"{Environment.NewLine}{Environment.NewLine}{Strings.AccessibilityPermissionRestartRequiredMessage}";
                if (!await _dialog.ShowConfirmationAsync(Strings.AccessibilityPermission,
                    message, Strings.RequestPermission, Strings.Cancel))
                    return;
                // Access may have been granted while the confirmation was open.
                if (getState().IsGranted())
                    return;
                try
                {
                    await _resetAccessibilityPermission();
                }
                catch (Exception ex)
                {
                    _logger.Write(nameof(MacOSPermissionProvider), $"Failed to reset accessibility permission: {ex.Message}");
                }
                _openAccessibilitySettings();
                _requestAccessibilityPermission();
            });
        }
        catch (Exception ex)
        {
            _logger.Write(nameof(MacOSPermissionProvider), $"Failed to request accessibility permission: {ex.Message}");
        }
        finally
        {
            Volatile.Write(ref _requestInProgress, 0);
        }
    }

    private async Task OpenSettingsAsync()
    {
        try
        {
            _openAccessibilitySettings();
        }
        catch (Exception ex)
        {
            _logger.Write(nameof(MacOSPermissionProvider), ex.Message);
            await _dialog.ShowMessageAsync(Strings.OpenSystemSettings, ex.Message, Strings.Confirm);
        }
    }

    private static PermissionState GetPermissionState(Func<bool> isAllowed)
    {
        try
        {
            return isAllowed() ? PermissionState.Available : PermissionState.Denied;
        }
        catch
        {
            return PermissionState.Unknown;
        }
    }
}
