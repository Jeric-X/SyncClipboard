namespace SyncClipboard.Abstract;

public interface IProfileDtoHelper
{
    //para1 dto, para2 extraFilePath
    Task<(string, string?)> CreateProfileDto(CancellationToken ctk);
    public void SetLocalClipboardWithDto(ClipboardProfileDTO profileDto, string fileFolder);
}
