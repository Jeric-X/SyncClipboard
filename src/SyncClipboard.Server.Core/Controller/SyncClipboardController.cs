using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SyncClipboard.Server.Core.Hubs;
using Microsoft.AspNetCore.StaticFiles;
using SyncClipboard.Abstract;
using System.Text.Json;
using SyncClipboard.Server.Core.Constants;

namespace SyncClipboard.Server.Core.Controller;


public class SyncClipboardController(IHubContext<SyncClipboardHub> hubContext)
{
    protected ClipboardProfileDTO? ProfileDtoCache = null;
    private readonly IHubContext<SyncClipboardHub> _hubContext = hubContext;

    private async Task<IResult> PutFile(HttpContext content, string rootPath, string path)
    {
        ProfileDtoCache = null;

        var pathFolder = Path.Combine(rootPath, "file");
        if (!Directory.Exists(pathFolder))
        {
            Directory.CreateDirectory(pathFolder);
        }
        using var fs = new FileStream(path, FileMode.Create);
        await content.Request.Body.CopyToAsync(fs);
        return Results.Ok();
    }

    public virtual Task<IResult> GetFile(string path)
    {
        if (!File.Exists(path))
        {
            return Task.FromResult(Results.NotFound());
        }
        new FileExtensionContentTypeProvider().TryGetContentType(path, out string? contentType);
        return Task.FromResult(Results.File(path, contentType));
    }

    private static IResult ExistOrCreateFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        return Results.Ok();
    }

    private static IResult DeleteFolder(string path)
    {
        if (Directory.Exists(path))
        {
            try
            {
                Directory.Delete(path, true);
            }
            catch { }
            return Results.Ok();
        }
        return Results.NotFound();
    }

    protected virtual async Task<ClipboardProfileDTO> GetSyncProfile(string rootPath, string path)
    {
        try
        {
            ProfileDtoCache ??= JsonSerializer.Deserialize<ClipboardProfileDTO>(await File.ReadAllTextAsync(Path.Combine(rootPath, path)));
            return ProfileDtoCache ?? new ClipboardProfileDTO();
        }
        catch (Exception)
        {
            return new ClipboardProfileDTO();
        }
    }

    protected virtual async Task<IResult> PutSyncProfile(ClipboardProfileDTO profileDTO, string rootPath, string path)
    {
        ProfileDtoCache = profileDTO;
        await File.WriteAllTextAsync(Path.Combine(rootPath, path), JsonSerializer.Serialize(profileDTO));
        await _hubContext.Clients.All.SendAsync(SignalRConstants.RemoteProfileChangedMethod, profileDTO);
        return Results.Ok();
    }

    public static void MapRoutes(WebApplication app)
    {
        var rootPath = app.Environment.WebRootPath;

        app.MapMethods("/", ["PROPFIND"], () =>
            Results.Ok()).ExcludeFromDescription().RequireAuthorization();

        app.MapMethods("/file", ["PROPFIND", "MKCOL"], () =>
            ExistOrCreateFolder(Path.Combine(rootPath, "file"))).ExcludeFromDescription().RequireAuthorization();

        app.MapDelete("/file", () =>
            DeleteFolder(Path.Combine(rootPath, "file"))).RequireAuthorization();

        app.MapMethods("/file/{fileName}", ["HEAD", "GET"], (string fileName, [FromServices] SyncClipboardController controller) =>
        {
            if (InvalidFileName(fileName))
            {
                return Task.FromResult(Results.BadRequest());
            }
            return controller.GetFile(Path.Combine(rootPath, "file", fileName));
        }).RequireAuthorization();

        app.MapPut("/file/{fileName}", async (HttpContext content, string fileName, [FromServices] SyncClipboardController controller) =>
        {
            if (InvalidFileName(fileName))
            {
                return Results.BadRequest();
            }
            return await controller.PutFile(content, rootPath, Path.Combine(rootPath, "file", fileName));
        }).RequireAuthorization();

        app.MapGet("/SyncClipboard.json", ([FromServices] SyncClipboardController controller) =>
            controller.GetSyncProfile(rootPath, "SyncClipboard.json")).RequireAuthorization();

        app.MapPut("/SyncClipboard.json", ([FromBody] ClipboardProfileDTO profileDto, [FromServices] SyncClipboardController controller) =>
            controller.PutSyncProfile(profileDto, rootPath, "SyncClipboard.json")).RequireAuthorization();

        app.MapHub<SyncClipboardHub>(SignalRConstants.HubPath);//.RequireAuthorization();

        app.MapGet("/{name}", (string name, [FromServices] SyncClipboardController controller) =>
        {
            if (InvalidFileName(name))
            {
                return Task.FromResult(Results.BadRequest());
            }
            return controller.GetFile(Path.Combine(rootPath, name));
        }).RequireAuthorization();

        app.MapPut("/{name}", async (HttpContext content, string name, [FromServices] SyncClipboardController controller) =>
        {
            if (InvalidFileName(name))
            {
                return Results.BadRequest();
            }
            return await controller.PutFile(content, rootPath, Path.Combine(rootPath, name));
        }).RequireAuthorization();

        app.MapGet("/", () =>
        {
            return "Server is running.";
        }).ExcludeFromDescription().RequireAuthorization();
    }

    public static bool InvalidFileName(string name)
    {
        return name.Contains('\\') || name.Contains('/');
    }
}