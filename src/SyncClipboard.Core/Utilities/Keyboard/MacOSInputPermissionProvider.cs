using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Keyboard;

namespace SyncClipboard.Core.Utilities.Keyboard;

public sealed class MacOSInputPermissionProvider : IInputPermissionProvider
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

    public MacOSInputPermissionProvider(ILogger logger, IThreadDispatcher dispatcher, IGlobalDialog dialog)
        : this(logger, dispatcher, dialog, MacOSAccessibility.OpenSettings, MacOSAccessibility.Request,
            MacOSAccessibility.ResetAsync, MacOSAccessibility.AXIsProcessTrusted, MacOSAccessibility.CGPreflightPostEventAccess)
    { }

    internal MacOSInputPermissionProvider(ILogger logger, IThreadDispatcher dispatcher, IGlobalDialog dialog,
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

    public InputPermissionStatus GetStatus()
    {
        var state = GetPermissionState(_isAccessibilityEnabled);
        // Avoid initializing the event-posting check until AX access is available.
        if (state == InputPermissionState.Available)
            state = GetPermissionState(_canPostEvents);
        // All app features share one accessibility permission, including window and caret access.
        return new(state, state, state);
    }

    public InputPermissionStatus GetSimulationStatus() => GetStatus();

    public IReadOnlyList<InputPermissionItem> GetPermissions()
    {
        var state = GetStatus().Accessibility;
        InputPermissionAction action = state == InputPermissionState.Available
            ? new(Strings.OpenSystemSettings, OpenSettingsAsync)
            : new(Strings.RequestPermission, RequestAccessibilityPermissionAsync);
        return [new(Strings.AccessibilityPermission, Strings.AccessibilityPermissionDescription, state, [action])];
    }

    public bool CheckAndRequestAccessibilityPermission()
    {
        if (GetStatus().CanSimulateInput)
            return true;
        DelegateExtention.SafeFireAndForget(RequestAccessibilityPermissionAsync, nameof(MacOSInputPermissionProvider));
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
                if (GetStatus().CanSimulateInput)
                    return;
                if (!await _dialog.ShowConfirmationAsync(Strings.AccessibilityPermission,
                    Strings.AccessibilityPermissionRequestMessage, Strings.RequestPermission, Strings.Cancel))
                    return;
                // Access may have been granted while the confirmation was open.
                if (GetStatus().CanSimulateInput)
                    return;
                try
                {
                    await _resetAccessibilityPermission();
                }
                catch (Exception ex)
                {
                    _logger.Write(nameof(MacOSInputPermissionProvider), $"Failed to reset accessibility permission: {ex.Message}");
                }
                _openAccessibilitySettings();
                _requestAccessibilityPermission();
            });
        }
        catch (Exception ex)
        {
            _logger.Write(nameof(MacOSInputPermissionProvider), $"Failed to request accessibility permission: {ex.Message}");
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
            _logger.Write(nameof(MacOSInputPermissionProvider), ex.Message);
            await _dialog.ShowMessageAsync(Strings.OpenSystemSettings, ex.Message, Strings.Confirm);
        }
    }

    private static InputPermissionState GetPermissionState(Func<bool> isAllowed)
    {
        try
        {
            return isAllowed() ? InputPermissionState.Available : InputPermissionState.Denied;
        }
        catch
        {
            return InputPermissionState.Unknown;
        }
    }
}
