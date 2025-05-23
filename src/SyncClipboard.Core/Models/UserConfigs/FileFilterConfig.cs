using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.Models.UserConfigs;

public record FileFilterConfig
{
    public string FileFilterMode { get; set; } = "";
    public List<string> WhiteList { get; set; } = [];
    public List<string> BlackList { get; set; } = [];

    public virtual bool Equals(FileFilterConfig? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null) return false;
        if (FileFilterMode != other.FileFilterMode) return false;
        if (WhiteList.Count != other.WhiteList.Count) return false;
        if (BlackList.Count != other.BlackList.Count) return false;
        if (WhiteList.Except(other.WhiteList).Any()) return false;
        if (BlackList.Except(other.BlackList).Any()) return false;

        return true;
    }

    public override int GetHashCode()
    {
        return new int[] { FileFilterMode.GetHashCode(), WhiteList.ListHashCode(), BlackList.ListHashCode() }.ListHashCode();
    }
}
