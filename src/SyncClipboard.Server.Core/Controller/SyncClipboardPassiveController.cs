using SyncClipboard.Abstract;

namespace SyncClipboard.Server.Controller;

public class SyncClipboardPassiveController : SyncClipboardController
{
    private readonly IProfileDtoHelper _profileDtoHelper;
    private ClipboardProfileDTO? _profileDtoCache;
    public SyncClipboardPassiveController(IServiceProvider services)
    {
        _profileDtoHelper = services.GetRequiredService<IProfileDtoHelper>();
    }

    protected override async Task<ClipboardProfileDTO> GetSyncProfile(string rootPath, string path)
    {
        var profileDto = await _profileDtoHelper.CreateProfileDto(_profileDtoCache, Path.Combine(rootPath, "file"));
        _profileDtoCache = profileDto;
        return profileDto;
    }

    protected override async Task<IResult> PutSyncProfile(ClipboardProfileDTO profileDTO, string rootPath, string path)
    {
        await _profileDtoHelper.SetLocalClipboardWithDto(profileDTO, Path.Combine(rootPath, "file"));
        return Results.Ok();
    }
}
