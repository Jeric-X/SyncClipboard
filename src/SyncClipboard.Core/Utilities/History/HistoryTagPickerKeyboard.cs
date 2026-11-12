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
        if (command)
            return false;

        if (TryMoveSelection(key, shift))
        {
            return true;
        }

        if (shift || !IsActivationKey(key))
            return false;

        if (activationKey is null && !isRepeat)
        {
            activationKey = key;
            operation = viewModel.ToggleSelectedAsync();
        }
        return true;
    }

    private bool TryMoveSelection(Key? key, bool shift)
    {
        if (key != Key.Tab && (shift || key is not (Key.Up or Key.Down)))
            return false;

        viewModel.MoveSelection(key == Key.Tab ? !shift : key == Key.Down);
        return true;
    }

    private bool IsActivationKey(Key? key) =>
        key == Key.Enter || (key == Key.Space && viewModel.Search.Length == 0);
}
