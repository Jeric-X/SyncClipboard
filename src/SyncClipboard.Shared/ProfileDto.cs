using SyncClipboard.Shared.Profiles;

namespace SyncClipboard.Shared;

public record class ProfileDto
{
    public ProfileType Type { get; set; } = ProfileType.Unknown;
    public string Hash { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool HasData { get; set; } = false;
    public string? DataName { get; set; }
}
