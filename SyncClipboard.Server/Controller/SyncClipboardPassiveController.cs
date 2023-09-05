namespace SyncClipboard.Server.Controller;

public class SyncClipboardPassiveController : SyncClipboardController
{
    private readonly IServiceProvider _services;

    public SyncClipboardPassiveController(IServiceProvider services)
    {
        _services = services;
    }

    protected override IResult GetSyncProfile(string path)
    {
        return base.GetSyncProfile(path);
    }

    protected override async Task<IResult> PutSyncProfile(HttpContext content, string path)
    {
        using var fs = new FileStream(path, FileMode.Create);
        await content.Request.Body.CopyToAsync(fs);
        return Results.Ok();
    }
}
