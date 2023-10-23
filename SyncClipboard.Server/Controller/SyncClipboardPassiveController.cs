using SyncClipboard.Abstract;

namespace SyncClipboard.Server.Controller;

public class SyncClipboardPassiveController : SyncClipboardController
{
    private readonly IProfileDtoHelper _profileDtoHelper;
    public SyncClipboardPassiveController(IServiceProvider services)
    {
        _profileDtoHelper = services.GetRequiredService<IProfileDtoHelper>();
    }

    protected override async Task<IResult> GetSyncProfile(string rootPath, string path)
    {
        var (dtoString, filePath) = await _profileDtoHelper.CreateProfileDto(CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(rootPath, path), dtoString);

        var fileFolder = Path.Combine(rootPath, "file");
        if (filePath != null)
        {
            if (Path.GetDirectoryName(filePath) != fileFolder)
            {
                if (Directory.Exists(fileFolder))
                {
                    Directory.Delete(fileFolder, true);
                }
                Directory.CreateDirectory(fileFolder);
                await Task.Run(() => File.Copy(filePath, Path.Combine(fileFolder, Path.GetFileName(filePath)), true));
            }
        }
        return await base.GetSyncProfile(rootPath, path);
    }

    protected override async Task<IResult> PutSyncProfile(HttpContext content, string rootPath, string path)
    {
        using StreamReader reader = new StreamReader(content.Request.Body);
        _profileDtoHelper.SetLocalClipboardWithDto(await reader.ReadToEndAsync(), Path.Combine(rootPath, "file"));
        return Results.Ok();
    }
}
