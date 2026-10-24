using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Permissions;

namespace SyncClipboard.Core.Utilities.Permissions;

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
        PermissionCapability.SimulateInput or PermissionCapability.MonitorKeyboard or
            PermissionCapability.ReadWindow or PermissionCapability.ReadCaret => GetAccessibilityState(),
        _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, null)
    };

    private PermissionState GetAccessibilityState()
    {
        var state = GetPermissionState(_isAccessibilityEnabled);
        // Avoid initializing the event-posting check until AX access is available.
        if (state == PermissionState.Available)
            state = GetPermissionState(_canPostEvents);
        // All app features share one accessibility permission, including window and caret access.
        return state;
    }

    public IReadOnlyList<PermissionItem> GetPermissions()
    {
        var state = GetAccessibilityState();
        PermissionAction action = state == PermissionState.Available
            ? new(Strings.OpenSystemSettings, OpenSettingsAsync)
            : new(Strings.RequestPermission, RequestAccessibilityPermissionAsync);
        return [new(Strings.AccessibilityPermission, Strings.AccessibilityPermissionDescription, state, [action])];
    }

    public bool CheckAndRequest(PermissionCapability capability)
    {
        if (GetState(capability).IsGranted())
            return true;
        DelegateExtention.SafeFireAndForget(RequestAccessibilityPermissionAsync, nameof(MacOSPermissionProvider));
        return false;
    }

    private async Task RequestAccessibilityPermissionAsync()
    {
        if (Interlocked.CompareExchange(ref _requestInProgress, 1, 0) != 0)
            return;
        try
        {
            await _dispatcher.RunOnMainThreadAsync(async () =>
            {
                if (GetAccessibilityState().IsGranted())
                    return;
                if (!await _dialog.ShowConfirmationAsync(Strings.AccessibilityPermission,
                    Strings.AccessibilityPermissionRequestMessage, Strings.RequestPermission, Strings.Cancel))
                    return;
                // Access may have been granted while the confirmation was open.
                if (GetAccessibilityState().IsGranted())
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
