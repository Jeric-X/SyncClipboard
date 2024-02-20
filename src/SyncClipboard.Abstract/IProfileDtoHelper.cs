namespace SyncClipboard.Abstract;

public interface IProfileDtoHelper
{
    //para1 dto, para2 extraFilePath
    Task<(ClipboardProfileDTO, string?)> CreateProfileDto(CancellationToken ctk);
    public Task SetLocalClipboardWithDto(ClipboardProfileDTO profileDto, string fileFolder);
}
