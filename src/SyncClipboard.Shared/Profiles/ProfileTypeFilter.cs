namespace SyncClipboard.Shared.Profiles;

[Flags]
public enum ProfileTypeFilter
{
    None = 0,
    Text = 1 << ProfileType.Text,
    File = 1 << ProfileType.File,
    Image = 1 << ProfileType.Image,
    Group = 1 << ProfileType.Group,
    Unknown = 1 << ProfileType.Unknown,
    All = Text | File | Image | Group | Unknown
}
