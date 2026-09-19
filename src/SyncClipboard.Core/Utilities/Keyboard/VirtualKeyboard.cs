using SharpHook;
using SharpHook.Data;
using SharpHook.Providers;
using SharpHook.Simulation;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.Keyboard;

namespace SyncClipboard.Core.Utilities.Keyboard;

public sealed class VirtualKeyboard : IDisposable
{
    private readonly IInputPermissionProvider _permissions;
    private readonly Func<IEventSimulator> _createSimulator;
    private readonly Lock _lock = new();
    private IEventSimulator? _simulator;
    private bool _disposed;

    public VirtualKeyboard(IInputPermissionProvider permissions) : this(permissions, CreateSimulator) { }

    internal VirtualKeyboard(IInputPermissionProvider permissions, Func<IEventSimulator> createSimulator)
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

    public void Copy() => SendShortcut(KeyCode.VcC);

    public void Paste() => SendShortcut(KeyCode.VcV);

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

    private void SendShortcut(KeyCode key)
    {
        Execute(simulator =>
        {
            var modifier = OperatingSystem.IsMacOS() ? KeyCode.VcLeftMeta : KeyCode.VcLeftControl;
            var result = UioHookResult.Success;
            try
            {
                result = simulator.SimulateKeyPress(modifier);
                if (result == UioHookResult.Success) result = simulator.SimulateKeyPress(key);
            }
            finally
            {
                // Release both keys even when part of the shortcut failed.
                try
                {
                    var keyResult = simulator.SimulateKeyRelease(key);
                    if (result == UioHookResult.Success) result = keyResult;
                }
                finally
                {
                    var modifierResult = simulator.SimulateKeyRelease(modifier);
                    if (result == UioHookResult.Success) result = modifierResult;
                }
            }
            EnsureSuccess(result);
        });
    }

    private void Execute(Action<IEventSimulator> action)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                if (!_permissions.GetStatus().CanSimulateInput)
                {
                    throw new InvalidOperationException(Strings.InputSimulationPermissionRequired);
                }

                // Failed initialization is not cached, so a later attempt can use newly granted access.
                _simulator ??= _createSimulator();
                action(_simulator);
            }
            catch
            {
                DisposeSimulator();
                throw;
            }
        }
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
