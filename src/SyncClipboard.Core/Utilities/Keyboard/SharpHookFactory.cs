using SharpHook;
using SharpHook.Data;
using SharpHook.Providers;
using SharpHook.Simulation;

namespace SyncClipboard.Core.Utilities.Keyboard;

public sealed class SharpHookFactory
{
    private readonly IEventSimulationProvider _simulationProvider;
    private readonly IGlobalHookProvider _globalHookProvider;

    public SharpHookFactory() : this(OperatingSystem.IsLinux(), UioHookProvider.Instance,
        UioHookProvider.Instance, UioHookProvider.Instance)
    {
    }

    internal SharpHookFactory(bool isLinux, ILinuxBackendProvider linuxBackendProvider,
        IEventSimulationProvider simulationProvider, IGlobalHookProvider globalHookProvider)
    {
        _simulationProvider = simulationProvider;
        _globalHookProvider = globalHookProvider;

        if (isLinux)
        {
            // Preserve the existing X11/XWayland behavior without requiring access to uinput devices.
            var result = linuxBackendProvider.SetLinuxMode(LinuxMode.XRecord);
            if (result != UioHookResult.Success)
                throw new HookException(result, $"Failed to select the XRecord keyboard backend: {result}");
        }
    }

    public IEventSimulator CreateEventSimulator() => EventSimulator.Create("SyncClipboard", _simulationProvider);

    public IGlobalHook CreateGlobalHook() => new SimpleGlobalHook(_globalHookProvider);
}
