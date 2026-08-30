namespace SyncClipboard.Core.Utilities.Runner;

public static class AsyncRunner
{
    public static async Task<bool> WaitForConditionAsync(
        Func<bool> condition,
        TimeSpan timeout,
        TimeSpan checkInterval,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(checkInterval, cancellationToken);
        }
        while (DateTime.UtcNow < deadline);

        return condition();
    }
}
