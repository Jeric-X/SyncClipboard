using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Core.Models.UserConfigs;

public record ClipboardOwnerFilterConfig
{
    public string FilterMode { get; set; } = "";
    public List<ForegroundWindowInfo> WhiteList { get; set; } = [];
    public List<ForegroundWindowInfo> BlackList { get; set; } = [];

    public virtual bool Equals(ClipboardOwnerFilterConfig? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null) return false;
        if (FilterMode != other.FilterMode) return false;
        if (WhiteList.Count != other.WhiteList.Count) return false;
        if (BlackList.Count != other.BlackList.Count) return false;
        if (!WhiteList.SequenceEqual(other.WhiteList)) return false;
        if (!BlackList.SequenceEqual(other.BlackList)) return false;

        return true;
    }

    public override int GetHashCode()
    {
        var hash = FilterMode.GetHashCode();
        foreach (var item in WhiteList)
        {
            hash = HashCode.Combine(hash, item.ProcessName, item.WindowTitle, item.ExecutableName);
        }
        foreach (var item in BlackList)
        {
            hash = HashCode.Combine(hash, item.ProcessName, item.WindowTitle, item.ExecutableName);
        }
        return hash;
    }
}
