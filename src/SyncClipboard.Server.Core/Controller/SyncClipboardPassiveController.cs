using SyncClipboard.Abstract;

namespace SyncClipboard.Server.Controller;

public class SyncClipboardPassiveController : SyncClipboardController, IDisposable
{
    private readonly IProfileDtoHelper _profileDtoHelper;
    private ClipboardProfileDTO? _profileDtoCache;
    private readonly IClipboardMoniter _clipboardMoniter;

    public SyncClipboardPassiveController(IServiceProvider services)
    {
        _profileDtoHelper = services.GetRequiredService<IProfileDtoHelper>();
        _clipboardMoniter = services.GetRequiredService<IClipboardMoniter>();
        _clipboardMoniter.ClipboardChanged += ClipboardChangd;
    }

    private void ClipboardChangd()
    {
        _profileDtoCache = null;
    }

    protected override async Task<ClipboardProfileDTO> GetSyncProfile(string rootPath, string path)
    {
        _profileDtoCache ??= await _profileDtoHelper.CreateProfileDto(Path.Combine(rootPath, "file"));
        return _profileDtoCache;
    }

    protected override async Task<IResult> PutSyncProfile(ClipboardProfileDTO profileDTO, string rootPath, string path)
    {
        _profileDtoCache = null;
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
