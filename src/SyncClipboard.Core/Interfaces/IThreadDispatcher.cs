namespace SyncClipboard.Core.Interfaces;

public interface IThreadDispatcher
{
    Task<T> RunOnMainThreadAsync<T>(Func<Task<T>> func);
    Task RunOnMainThreadAsync(Func<Task> func);
}
