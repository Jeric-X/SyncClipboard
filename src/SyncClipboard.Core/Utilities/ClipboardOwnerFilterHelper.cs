using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Utilities;

public static class ClipboardOwnerFilterHelper
{
    public static bool ShouldFilter(ClipboardOwnerFilterConfig config, ForegroundWindowInfo? owner)
    {
        if (string.IsNullOrEmpty(config.FilterMode))
        {
            return false;
        }

        if (config.FilterMode == "BlackList")
        {
            if (!owner.HasValue)
            {
                return false;
            }

            var ownerValue = owner.Value;
            foreach (var item in config.BlackList)
            {
                if (Matches(item, ownerValue))
                {
                    return true;
                }
            }
            return false;
        }

        if (config.FilterMode == "WhiteList")
        {
            if (!owner.HasValue)
            {
                return true;
            }

            if (config.WhiteList.Count == 0)
            {
                return false;
            }

            var ownerValue = owner.Value;
            foreach (var item in config.WhiteList)
            {
                if (Matches(item, ownerValue))
                {
                    return false;
                }
            }
            return true;
        }

        return false;
    }

    private static bool Matches(ForegroundWindowInfo filter, ForegroundWindowInfo target)
    {
        if (!string.IsNullOrEmpty(filter.ProcessName) && filter.ProcessName != target.ProcessName)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(filter.WindowTitle) && filter.WindowTitle != target.WindowTitle)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(filter.ExecutableName) && filter.ExecutableName != target.ExecutableName)
        {
            return false;
        }

        if (string.IsNullOrEmpty(filter.ProcessName) && string.IsNullOrEmpty(filter.WindowTitle) && string.IsNullOrEmpty(filter.ExecutableName))
        {
            return false;
        }

        return true;
    }
}
