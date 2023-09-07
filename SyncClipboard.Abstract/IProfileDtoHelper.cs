namespace SyncClipboard.Abstract;

public interface IProfileDtoHelper
{
    string CreateProfileDto(out string? extraFilePath);
    public void SetLocalClipboardWithDto(string profileDto, string fileFolder);
}
