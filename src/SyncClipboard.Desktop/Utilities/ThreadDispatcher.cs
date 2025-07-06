using Avalonia.Threading;
using SyncClipboard.Core.Interfaces;
using System;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.Utilities;

internal class ThreadDispatcher : IThreadDispatcher
{
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
