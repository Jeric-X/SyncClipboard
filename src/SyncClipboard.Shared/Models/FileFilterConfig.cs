using SyncClipboard.Shared.Attributes;
using SyncClipboard.Shared.Interfaces;
using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Shared.Models;

[ConfigKey(ConfigKey, ConfigStorage.SyncClipboard)]
public record FileFilterConfig : IConfigValidator
{
    public const string ConfigKey = "FileFilter";

    public string FileFilterMode { get; set; } = "";
    public List<FileFilterRule> WhiteList { get; set; } = [];
    public List<FileFilterRule> BlackList { get; set; } = [];

    public void Validate()
    {
        if (FileFilterMode is not ("" or "BlackList" or "WhiteList"))
        {
            throw new InvalidDataException($"FileFilter contains an unsupported filter mode '{FileFilterMode}'.");
        }

        foreach (var rule in WhiteList.Concat(BlackList))
        {
            if (!FileFilterHelper.TryValidateRule(rule, out var error))
            {
                throw new InvalidDataException($"FileFilter contains an invalid rule: {error}");
            }
        }
    }

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
