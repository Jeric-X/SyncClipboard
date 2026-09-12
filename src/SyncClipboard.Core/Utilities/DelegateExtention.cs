namespace SyncClipboard.Core.Utilities;

public static class DelegateExtention
{
    /// <summary>
    /// 发起异步操作并记录异常，默认最多等待五分钟，调用方无需等待。
    /// 超时仅停止等待，不取消底层操作。
    /// </summary>
    public static void SafeFireAndForget(
        this Func<Task> action,
        string? logTag = null,
        TimeSpan? timeout = null)
    {
        _ = SafeFireAndForgetCoreAsync(action, logTag, timeout);
    }

    internal static async Task SafeFireAndForgetCoreAsync(
        Func<Task> action,
        string? logTag = null,
        TimeSpan? timeout = null)
    {
        try
        {
            await action().WaitAsync(timeout ?? TimeSpan.FromMinutes(5));
        }
        catch (Exception ex)
        {
            AppCore.TryGetCurrent()?.Logger.Write(logTag, $"Invoke Ignore Exception {ex}");
        }
    }

    public static Action NoExcept(this Action action, string? logTag = null)
    {
        return () =>
        {
            try
            {
                action.Invoke();
            }
            catch (Exception ex)
            {
                AppCore.Current?.Logger.Write(logTag, $"Invoke Ignore Exception {ex.Message}\n{ex.StackTrace}");
            }
        };
    }

    public static Action<T> NoExcept<T>(this Action<T> action, string? logTag = null)
    {
        return arg =>
        {
            try
            {
                action.Invoke(arg);
            }
            catch (Exception ex)
            {
                AppCore.Current?.Logger.Write(logTag, $"Invoke Ignore Exception {ex.Message}\n{ex.StackTrace}");
            }
        };
    }

    public static void InvokeNoExcept(this Action action, string? logTag = null)
    {
        try
        {
            action.Invoke();
        }
        catch (Exception ex)
        {
            AppCore.Current?.Logger.Write(logTag, $"Invoke Ignore Exception {ex.Message}\n{ex.StackTrace}");
        }
    }

    public static void InvokeNoExcept(this Delegate dele, params object?[]? args)
    {
        try
        {
            dele.DynamicInvoke(args);
        }
        catch (Exception ex)
        {
            AppCore.Current?.Logger.Write($"Invoke Ignore Exception {ex.Message}\n{ex.StackTrace}");
        }
    }
}
