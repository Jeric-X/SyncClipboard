using SharpHook;
using SharpHook.Data;
using SharpHook.Providers;
using SharpHook.Simulation;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Models.Permissions;
using System.Runtime.ExceptionServices;

namespace SyncClipboard.Core.Utilities.Keyboard;

public sealed class VirtualKeyboard : IDisposable
{
    private readonly IPermissionProvider _permissions;
    private readonly Func<IEventSimulator> _createSimulator;
    private readonly Lock _lock = new();
    private IEventSimulator? _simulator;
    private bool _disposed;
    private bool _permissionRequestAttempted;

    public VirtualKeyboard(IPermissionProvider permissions) : this(permissions, CreateSimulator)
    { }

    internal VirtualKeyboard(IPermissionProvider permissions, Func<IEventSimulator> createSimulator)
    {
        _permissions = permissions;
        _createSimulator = createSimulator;
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
        if (hotkey.Keys.Length == 0)
            return;

        Execute(simulator =>
        {
            var result = UioHookResult.Success;
            foreach (var key in hotkey.Keys)
            {
                var releaseResult = simulator.SimulateKeyRelease(KeyCodeMap.MapReverse[key]);
                if (result == UioHookResult.Success)
                    result = releaseResult;
            }
            EnsureSuccess(result);
        });
    }

    public void SendShortcut(params KeyCode[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Length == 0)
            return;

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
            if (error is not null)
                ExceptionDispatchInfo.Throw(error);
        });
    }

    private void Execute(Action<IEventSimulator> action)
    {
        var requestPermission = false;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                if (_permissions.GetState(PermissionCapability.SimulateInput).IsGranted())
                {
                    // Failed initialization is not cached, so a later attempt can use newly granted access.
                    _simulator ??= _createSimulator();
                    action(_simulator);
                    return;
                }

                if (!_permissionRequestAttempted)
                {
                    // This singleton allows one automatic attempt per app run, even after cancellation or failure.
                    _permissionRequestAttempted = true;
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
            _permissions.CheckAndRequest(PermissionCapability.SimulateInput);
        // Request access for a future attempt; never replay the input that triggered the request.
        throw new InvalidOperationException(Strings.InputSimulationPermissionRequired);
    }

    private static void EnsureSuccess(UioHookResult result)
    {
        if (result != UioHookResult.Success)
            throw new HookException(result);
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
            if (_disposed)
                return;
            _disposed = true;
            DisposeSimulator();
        }
    }
}
