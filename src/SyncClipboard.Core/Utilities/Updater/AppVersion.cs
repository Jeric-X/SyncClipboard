using System;
using System.Text.RegularExpressions;

namespace SyncClipboard.Core.Utilities.Updater;

public class AppVersion : IComparable<AppVersion>
{
    private readonly List<int> _versions = new();
    private readonly int? _betaNum = null;

    public static AppVersion Parse(string versionStr)
    {
        var expression = @"^v?(?'verNum'\d+\.\d+\.\d+(\.\d)?)(-beta(?'betaNum'\d+))?$";
        var match = Regex.Match(versionStr, expression);
        if (match.Success)
        {
            var verNumStr = match.Groups["verNum"].Value;
            var betaNum = match.Groups["betaNum"];
            return new AppVersion(verNumStr, betaNum.Success ? betaNum.Value : null);
        }

        throw new ArgumentException("Can not parse the string to Version");
    }

    public int CompareTo(AppVersion? other)
    {
        if (other is null)
            return 1;

        for (int i = 0; i < Math.Min(_versions.Count, other._versions.Count); i++)
        {
            if (_versions[i] != other._versions[i])
                return _versions[i].CompareTo(other._versions[i]);
        }

        if (_versions.Count != other._versions.Count)
            return _versions.Count.CompareTo(other._versions.Count);

        if (_betaNum == other._betaNum)
            return 0;

        if (_betaNum != null && other._betaNum != null)
            return _betaNum.Value.CompareTo(other._betaNum.Value);

        if (_betaNum is null)
            return 1;

        return -1;
    }

    private AppVersion(string versionNum, string? betaNum)
    {
        foreach (var num in versionNum.Split('.'))
        {
            _versions.Add(Convert.ToInt32(num));
        }

        if (betaNum is not null)
            _betaNum = Convert.ToInt32(betaNum);
    }

    public static bool operator >(AppVersion appVersion, AppVersion other)
    {
        if (other is null) return false;
        return appVersion?.CompareTo(other) > 0;
    }
    public static bool operator >=(AppVersion appVersion, AppVersion other)
    {
        if (other is null) return false;
        return appVersion?.CompareTo(other) >= 0;
    }
    public static bool operator <(AppVersion appVersion, AppVersion other)
    {
        if (other is null) return false;
        return appVersion?.CompareTo(other) < 0;
    }
    public static bool operator <=(AppVersion appVersion, AppVersion other)
    {
        if (other is null) return false;
        return appVersion?.CompareTo(other) <= 0;
    }
}
