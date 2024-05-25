namespace SyncClipboard.Abstract;

public interface IProfileDtoHelper
{
    Task<ClipboardProfileDTO> CreateProfileDto(string destFolder);
    Task SetLocalClipboardWithDto(ClipboardProfileDTO profileDto, string fileFolder);
}
