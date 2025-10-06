using Avalonia.Threading;
using SyncClipboard.Core.Interfaces;
using System;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.Utilities;

internal class ThreadDispatcher : IThreadDispatcher
{
    public bool IsMainThread => Dispatcher.UIThread.CheckAccess();

    public async Task RunOnMainThreadAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }
        await Dispatcher.UIThread.InvokeAsync(action);
    }

    public Task<T> RunOnMainThreadAsync<T>(Func<Task<T>> func)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return func();
        }
        return Dispatcher.UIThread.InvokeAsync(func);
    }

    public Task RunOnMainThreadAsync(Func<Task> func)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return func();
        }
        return Dispatcher.UIThread.InvokeAsync(func);
    }
}
