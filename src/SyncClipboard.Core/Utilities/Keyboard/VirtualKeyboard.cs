using SharpHook.Data;
using SharpHook.Simulation;

namespace SyncClipboard.Core.Utilities.Keyboard;

public class VirtualKeyboard(IEventSimulator _keyEventSimulator)
{
    public void Paste()
    {
        KeyCode modifier = OperatingSystem.IsMacOS() ? KeyCode.VcLeftMeta : KeyCode.VcLeftControl;

        _keyEventSimulator.SimulateKeyPress(modifier);
        _keyEventSimulator.SimulateKeyPress(KeyCode.VcV);

        _keyEventSimulator.SimulateKeyRelease(KeyCode.VcV);
        _keyEventSimulator.SimulateKeyRelease(modifier);
    }
}
