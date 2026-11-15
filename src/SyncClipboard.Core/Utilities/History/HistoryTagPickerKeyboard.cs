using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Core.Utilities.History;

public sealed class HistoryTagPickerKeyboard(HistoryTagPickerViewModel viewModel)
{
    private Key? activationKey;

    public bool HandleKeyUp(Key? key)
    {
        if (activationKey is null || key != activationKey)
            return false;
        activationKey = null;
        return true;
    }

    // Report whether the native event is handled before the view awaits the save.
    public bool TryHandleKeyDown(Key? key, bool shift, bool command, bool isRepeat, out Task operation)
    {
        operation = Task.CompletedTask;
        if (command || shift)
            return false;

        if (key is Key.Up or Key.Down)
        {
            viewModel.MoveSelection(key == Key.Down);
            return true;
        }

        if (!IsActivationKey(key))
            return false;

        if (activationKey is null && !isRepeat)
        {
            activationKey = key;
            operation = viewModel.ToggleSelectedAsync();
        }
        return true;
    }

    private bool IsActivationKey(Key? key) =>
        key is Key.Enter or Key.Tab || (key == Key.Space && viewModel.Search.Length == 0);
}
