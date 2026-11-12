using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Test.Client.History;

[TestClass]
public class HistoryTagPickerKeyboardTests
{
    [TestMethod]
    [DataRow(Key.Tab, false, "beta")]
    [DataRow(Key.Tab, true, "gamma")]
    [DataRow(Key.Down, false, "beta")]
    [DataRow(Key.Up, false, "gamma")]
    public async Task Navigation_SelectsAdjacentCandidateWithoutSaving(Key key, bool shift, string expected)
    {
        var saves = 0;
        var picker = new HistoryTagPickerViewModel(["alpha", "beta", "gamma"], [], (_, _) =>
        {
            saves++;
            return Task.CompletedTask;
        });
        var keyboard = new HistoryTagPickerKeyboard(picker);

        Assert.IsTrue(keyboard.TryHandleKeyDown(key, shift, false, false, out var operation));
        await operation;

        Assert.AreEqual(expected, picker.SelectedSuggestion?.Name);
        Assert.AreEqual(0, saves);
    }

    [TestMethod]
    [DataRow(Key.Space, "alpha", false, false)]
    [DataRow(Key.Enter, "", true, false)]
    [DataRow(Key.Tab, "", false, true)]
    [DataRow(Key.Up, "", true, false)]
    [DataRow(Key.A, "", false, false)]
    [DataRow(Key.PROCESSKEY, "", false, false)]
    public async Task TextAndModifiedKeys_RemainAvailableToNativeControls(Key key, string search, bool shift, bool command)
    {
        var saves = 0;
        var picker = new HistoryTagPickerViewModel(["alpha", "beta"], [], (_, _) =>
        {
            saves++;
            return Task.CompletedTask;
        })
        {
            Search = search
        };
        var selected = picker.SelectedSuggestion;
        var keyboard = new HistoryTagPickerKeyboard(picker);

        Assert.IsFalse(keyboard.TryHandleKeyDown(key, shift, command, false, out var operation));
        await operation;

        Assert.AreSame(selected, picker.SelectedSuggestion);
        Assert.AreEqual(search, picker.Search);
        Assert.AreEqual(0, saves);
    }

    [TestMethod]
    public async Task Activation_TogglesOnceUntilTheOriginalKeyIsReleased()
    {
        var saved = new List<bool>();
        var picker = new HistoryTagPickerViewModel(["alpha"], [], (_, add) =>
        {
            saved.Add(add);
            return Task.CompletedTask;
        });
        var keyboard = new HistoryTagPickerKeyboard(picker);

        Assert.IsTrue(keyboard.TryHandleKeyDown(Key.Enter, false, false, true, out var repeat));
        await repeat;
        Assert.IsEmpty(saved);

        Assert.IsTrue(keyboard.TryHandleKeyDown(Key.Space, false, false, false, out var first));
        await first;
        Assert.IsTrue(keyboard.TryHandleKeyDown(Key.Space, false, false, true, out repeat));
        await repeat;
        Assert.IsFalse(keyboard.HandleKeyUp(Key.Enter));
        Assert.IsTrue(keyboard.TryHandleKeyDown(Key.Enter, false, false, false, out repeat));
        await repeat;
        Assert.HasCount(1, saved);
        Assert.IsTrue(saved[0]);

        Assert.IsTrue(keyboard.HandleKeyUp(Key.Space));
        Assert.IsTrue(keyboard.TryHandleKeyDown(Key.Enter, false, false, false, out var second));
        await second;
        Assert.HasCount(2, saved);
        Assert.IsTrue(saved[0]);
        Assert.IsFalse(saved[1]);
    }
}
