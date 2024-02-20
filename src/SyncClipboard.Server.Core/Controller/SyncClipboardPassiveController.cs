using SyncClipboard.Abstract;

namespace SyncClipboard.Server.Controller;

public class SyncClipboardPassiveController : SyncClipboardController
{
    private readonly IProfileDtoHelper _profileDtoHelper;
    public SyncClipboardPassiveController(IServiceProvider services)
    {
        _profileDtoHelper = services.GetRequiredService<IProfileDtoHelper>();
    }

    protected override async Task<ClipboardProfileDTO> GetSyncProfile(string rootPath, string path)
    {
        var (profileDto, filePath) = await _profileDtoHelper.CreateProfileDto(CancellationToken.None);

        var fileFolder = Path.Combine(rootPath, "file");
        if (filePath != null)
        {
            if (Path.GetDirectoryName(filePath) != fileFolder)
            {
                if (Directory.Exists(fileFolder))
                {
                    try
                    {
                        Directory.Delete(fileFolder, true);
                    }
                    catch { }
                }
                Directory.CreateDirectory(fileFolder);
                await Task.Run(() => File.Copy(filePath, Path.Combine(fileFolder, Path.GetFileName(filePath)), true));
            }
        }
        return profileDto;
    }

    protected override async Task<IResult> PutSyncProfile(ClipboardProfileDTO profileDTO, string rootPath, string path)
    {
        await _profileDtoHelper.SetLocalClipboardWithDto(profileDTO, Path.Combine(rootPath, "file"));
        return Results.Ok();
    }
}
