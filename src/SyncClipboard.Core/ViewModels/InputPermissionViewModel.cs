using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models.Keyboard;

namespace SyncClipboard.Core.ViewModels;

public sealed class InputPermissionViewModel(InputPermissionItem permission, Action refresh)
{
    public string Name => permission.Name;
    public string Description => permission.Description;
    public InputPermissionState State => permission.State;
    public string StateText => GetPermissionText(State);
    public IReadOnlyList<InputPermissionActionViewModel> Actions { get; } = permission.Actions
        .Select(action => new InputPermissionActionViewModel(action, refresh)).ToArray();

    internal static string GetPermissionText(InputPermissionState state) => state switch
    {
        InputPermissionState.Available => Strings.InputPermissionAvailable,
        InputPermissionState.Denied => Strings.InputPermissionDenied,
        InputPermissionState.Unavailable => Strings.InputPermissionUnavailable,
        InputPermissionState.Partial => Strings.InputPermissionPartial,
        InputPermissionState.NotRequired => Strings.InputPermissionNotRequired,
        _ => Strings.InputPermissionUnknown
    };
}

public sealed class InputPermissionActionViewModel(InputPermissionAction action, Action refresh)
{
    public string Text => action.Text;
    public IAsyncRelayCommand Command { get; } = new AsyncRelayCommand(async () =>
    {
        await action.ExecuteAsync();
        refresh();
    });
}
