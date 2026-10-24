using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models.Permissions;

namespace SyncClipboard.Core.ViewModels;

public sealed class PermissionViewModel(PermissionItem permission, Action refresh)
{
    public string Name => permission.Name;
    public string Description => permission.Description;
    public PermissionState State => permission.State;
    public string StateText => GetPermissionText(State);
    public IReadOnlyList<PermissionActionViewModel> Actions { get; } = permission.Actions
        .Select(action => new PermissionActionViewModel(action, refresh)).ToArray();

    internal static string GetPermissionText(PermissionState state) => state switch
    {
        PermissionState.Available => Strings.InputPermissionAvailable,
        PermissionState.Denied => Strings.InputPermissionDenied,
        PermissionState.Unavailable => Strings.InputPermissionUnavailable,
        PermissionState.Partial => Strings.InputPermissionPartial,
        PermissionState.NotRequired => Strings.InputPermissionNotRequired,
        _ => Strings.InputPermissionUnknown
    };
}

public sealed class PermissionActionViewModel(PermissionAction action, Action refresh)
{
    public string Text => action.Text;
    public IAsyncRelayCommand Command { get; } = new AsyncRelayCommand(async () =>
    {
        await action.ExecuteAsync();
        refresh();
    });
}
