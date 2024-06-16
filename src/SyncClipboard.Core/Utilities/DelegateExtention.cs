namespace SyncClipboard.Core.Utilities;

public static class DelegateExtention
{
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
                AppCore.Current?.Logger.Write(logTag, $"Invoke Unhandled Exception {ex.Message}\n{ex.StackTrace}");
            }
        };
    }

    public static Action<T> NoExcept<T>(this Action<T> action, string? logTag = null)
    {
        return (T arg) =>
        {
            try
            {
                action.Invoke(arg);
            }
            catch (Exception ex)
            {
                AppCore.Current?.Logger.Write(logTag, $"Invoke Unhandled Exception {ex.Message}\n{ex.StackTrace}");
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
            AppCore.Current?.Logger.Write(logTag, $"Invoke Unhandled Exception {ex.Message}\n{ex.StackTrace}");
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
            AppCore.Current?.Logger.Write($"Invoke Unhandled Exception {ex.Message}\n{ex.StackTrace}");
        }
    }
}