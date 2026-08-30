using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Utilities;

public static class ClipboardOwnerFilterHelper
{
    public static bool ShouldFilter(ClipboardOwnerFilterConfig config, WindowInfo? owner)
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
                if (ForegroundWindowMatcher.Matches(item, ownerValue))
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
                if (ForegroundWindowMatcher.Matches(item, ownerValue))
                {
                    return false;
                }
            }
            return true;
        }

        return false;
    }
}
