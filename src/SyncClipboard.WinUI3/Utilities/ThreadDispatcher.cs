using CommunityToolkit.WinUI;
using Microsoft.UI.Dispatching;
using SyncClipboard.Core.Interfaces;
using System;
using System.Threading.Tasks;

namespace SyncClipboard.WinUI3.Utilities;

internal class ThreadDispatcher : IThreadDispatcher
{
    private readonly DispatcherQueue _dispatcherQueue;
    public ThreadDispatcher(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
    }

    public Task<T> RunOnMainThreadAsync<T>(Func<Task<T>> func)
    {
        return _dispatcherQueue.EnqueueAsync(func);
    }

    public Task RunOnMainThreadAsync(Func<Task> func)
    {
        return _dispatcherQueue.EnqueueAsync(func);
    }
}
