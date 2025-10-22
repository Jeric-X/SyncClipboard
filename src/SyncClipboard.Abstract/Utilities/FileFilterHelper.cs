using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using SyncClipboard.Abstract.Models;

namespace SyncClipboard.Abstract.Utilities;

public static class FileFilterHelper
{
    public static bool IsFileAvailableAfterFilter(string fileName, FileFilterConfig filterConfig)
    {
        if (filterConfig.FileFilterMode == "BlackList")
        {
            var str = filterConfig.BlackList.Find(str => fileName.EndsWith(str, StringComparison.OrdinalIgnoreCase));
            if (str is not null)
            {
                return false;
            }
        }
        else if (filterConfig.FileFilterMode == "WhiteList")
        {
            var str = filterConfig.WhiteList.Find(str => fileName.EndsWith(str, StringComparison.OrdinalIgnoreCase));
            if (str is null)
            {
                return false;
            }
        }
        return true;
    }
}
