namespace SyncClipboard.Abstract;

public interface IProfileDtoHelper
{
    Task<ClipboardProfileDTO> CreateProfileDto(ClipboardProfileDTO? existed, string destFolder);
    Task SetLocalClipboardWithDto(ClipboardProfileDTO profileDto, string fileFolder);
}
