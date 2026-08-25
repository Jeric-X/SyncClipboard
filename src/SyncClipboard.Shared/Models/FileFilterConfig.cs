using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Shared.Models;

public record FileFilterConfig
{
    public string FileFilterMode { get; set; } = "";
    public List<FileFilterRule> WhiteList { get; set; } = [];
    public List<FileFilterRule> BlackList { get; set; } = [];

    public virtual bool Equals(FileFilterConfig? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null) return false;
        if (FileFilterMode != other.FileFilterMode) return false;
        if (WhiteList.Count != other.WhiteList.Count) return false;
        if (BlackList.Count != other.BlackList.Count) return false;
        if (!WhiteList.SequenceEqual(other.WhiteList)) return false;
        if (!BlackList.SequenceEqual(other.BlackList)) return false;

        return true;
    }

    public override int GetHashCode()
    {
        return new int[] { FileFilterMode.GetHashCode(), WhiteList.ListHashCode(), BlackList.ListHashCode() }.ListHashCode();
    }
}
