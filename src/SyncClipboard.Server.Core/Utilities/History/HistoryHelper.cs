namespace SyncClipboard.Server.Core.Utilities.History;

public static class HistoryHelper
{
    public static bool ShouldUpdate(
        int oldVersion,
        int newVersion,
        DateTimeOffset oldLastModified,
        DateTimeOffset newLastModified,
        TimeSpan? threshold = null)
    {
        var th = threshold ?? TimeSpan.FromMinutes(5);

        var oldUtc = oldLastModified.ToUniversalTime();
        var newUtc = newLastModified.ToUniversalTime();

        var gap = (newUtc - oldUtc).Duration();
        var withinThreshold = gap <= th;

        if (withinThreshold)
        {
            return newVersion >= oldVersion;
        }
        else
        {
            return newUtc >= oldUtc;
        }
    }
}

