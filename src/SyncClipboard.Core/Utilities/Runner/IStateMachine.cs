namespace SyncClipboard.Core.Utilities.Runner;

public interface IStateMachine<TState>
{
    TState CurrentState { get; }
    event Action<TState>? StateChanged;
}