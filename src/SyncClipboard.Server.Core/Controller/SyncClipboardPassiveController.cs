using SyncClipboard.Abstract;

namespace SyncClipboard.Server.Controller;

public class SyncClipboardPassiveController : SyncClipboardController, IDisposable
{
    private readonly IProfileDtoHelper _profileDtoHelper;
    private readonly IClipboardMoniter _clipboardMoniter;

    public SyncClipboardPassiveController(IServiceProvider services)
    {
        _profileDtoHelper = services.GetRequiredService<IProfileDtoHelper>();
        _clipboardMoniter = services.GetRequiredService<IClipboardMoniter>();
        _clipboardMoniter.ClipboardChanged += ClipboardChangd;
    }

    private void ClipboardChangd()
    {
        ProfileDtoCache = null;
    }

    protected override async Task<ClipboardProfileDTO> GetSyncProfile(string rootPath, string path)
    {
        ProfileDtoCache ??= await _profileDtoHelper.CreateProfileDto(Path.Combine(rootPath, "file"));
        return ProfileDtoCache;
    }

    protected override async Task<IResult> PutSyncProfile(ClipboardProfileDTO profileDTO, string rootPath, string path)
    {
        ProfileDtoCache = profileDTO;
        await _profileDtoHelper.SetLocalClipboardWithDto(profileDTO, Path.Combine(rootPath, "file"));
        return Results.Ok();
    }

    public void Dispose()
    {
        _clipboardMoniter.ClipboardChanged -= ClipboardChangd;
        GC.SuppressFinalize(this);
    }

    ~SyncClipboardPassiveController() => Dispose();
}
