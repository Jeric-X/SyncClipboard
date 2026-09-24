using SharpHook;
using SharpHook.Data;
using SharpHook.Providers;
using SharpHook.Simulation;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Keyboard;
using System.Runtime.ExceptionServices;

namespace SyncClipboard.Core.Utilities.Keyboard;

public sealed class VirtualKeyboard : IDisposable
{
    private readonly IInputPermissionProvider _permissions;
    private readonly Func<IEventSimulator> _createSimulator;
    private readonly Action? _showPermissionNotice;
    private readonly Lock _lock = new();
    private IEventSimulator? _simulator;
    private bool _disposed;
    private bool _permissionPrompted;

    public VirtualKeyboard(IInputPermissionProvider permissions, IThreadDispatcher dispatcher, IGlobalDialog dialog)
        : this(permissions, CreateSimulator, () => DelegateExtention.SafeFireAndForget(
            () => dispatcher.RunOnMainThreadAsync(() => dialog.ShowMessageAsync(
                Strings.PermissionManagement, Strings.LinuxInputSimulationPermissionRequired, Strings.Confirm)),
            nameof(VirtualKeyboard)))
    { }

    internal VirtualKeyboard(IInputPermissionProvider permissions, Func<IEventSimulator> createSimulator,
        Action? showPermissionNotice = null)
    {
        _permissions = permissions;
        _createSimulator = createSimulator;
        _showPermissionNotice = showPermissionNotice;
    }

    private static IEventSimulator CreateSimulator()
    {
        if (OperatingSystem.IsMacOS())
        {
            UioHookProvider.Instance.PromptUserIfAxApiDisabled = false;
        }
        return EventSimulator.Create(Env.SoftName);
    }

    public void Copy()
    {
        var modifier = OperatingSystem.IsMacOS() ? KeyCode.VcLeftMeta : KeyCode.VcLeftControl;
        SendShortcut(modifier, KeyCode.VcC);
    }

    public void Paste()
    {
        var modifier = OperatingSystem.IsMacOS() ? KeyCode.VcLeftMeta : KeyCode.VcLeftControl;
        SendShortcut(modifier, KeyCode.VcV);
    }

    public void ReleaseKeys(Hotkey hotkey)
    {
        if (hotkey.Keys.Length == 0) return;

        Execute(simulator =>
        {
            var result = UioHookResult.Success;
            foreach (var key in hotkey.Keys)
            {
                var releaseResult = simulator.SimulateKeyRelease(KeyCodeMap.MapReverse[key]);
                if (result == UioHookResult.Success) result = releaseResult;
            }
            EnsureSuccess(result);
        });
    }

    public void SendShortcut(params KeyCode[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Length == 0) return;

        Execute(simulator =>
        {
            Exception? error = null;
            var attempted = 0;
            try
            {
                foreach (var key in keys)
                {
                    // Also release a key whose press failed: it may have partially reached the OS.
                    attempted++;
                    EnsureSuccess(simulator.SimulateKeyPress(key));
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                for (var i = attempted - 1; i >= 0; i--)
                {
                    try
                    {
                        EnsureSuccess(simulator.SimulateKeyRelease(keys[i]));
                    }
                    catch (Exception ex)
                    {
                        // Keep releasing the remaining keys and preserve the first failure.
                        error ??= ex;
                    }
                }
            }
            if (error is not null) ExceptionDispatchInfo.Throw(error);
        });
    }

    private void Execute(Action<IEventSimulator> action)
    {
        var requestPermission = false;
        InputPermissionStatus status;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                status = _permissions.GetSimulationStatus();
                if (status.CanSimulateInput)
                {
                    // Failed initialization is not cached, so a later attempt can use newly granted access.
                    _simulator ??= _createSimulator();
                    action(_simulator);
                    return;
                }

                if (!_permissionPrompted)
                {
                    // This service is a singleton. A failed request must not prompt again on the next input.
                    _permissionPrompted = true;
                    requestPermission = true;
                }
                DisposeSimulator();
            }
            catch
            {
                DisposeSimulator();
                throw;
            }
        }

        if (requestPermission)
        {
            if (status.Accessibility != InputPermissionState.NotRequired)
            {
                _permissions.CheckAndRequestAccessibilityPermission();
            }
            else
            {
                _showPermissionNotice?.Invoke();
            }
        }
        // Request access for a future attempt; never replay the input that triggered the request.
        throw new InvalidOperationException(Strings.InputSimulationPermissionRequired);
    }

    private static void EnsureSuccess(UioHookResult result)
    {
        if (result != UioHookResult.Success) throw new HookException(result);
    }

    private void DisposeSimulator()
    {
        var simulator = _simulator;
        _simulator = null;
        simulator?.Dispose();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            DisposeSimulator();
        }
    }
}
