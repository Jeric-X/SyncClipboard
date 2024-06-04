using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using SyncClipboard.Abstract;
using System.Text.Json;

namespace SyncClipboard.Server.Controller;

public class SyncClipboardController
{
    protected ClipboardProfileDTO? ProfileDtoCache = null;

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

    private static Task<IResult> GetFile(string path)
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
        return Results.Ok();
    }

    public void Route(WebApplication app)
    {
        var rootPath = app.Environment.WebRootPath;

        app.MapMethods("/", new string[] { "PROPFIND" }, () =>
            Results.Ok()).ExcludeFromDescription().RequireAuthorization();

        app.MapMethods("/file", new string[] { "PROPFIND", "MKCOL" }, () =>
            ExistOrCreateFolder(Path.Combine(rootPath, "file"))).ExcludeFromDescription().RequireAuthorization();

        app.MapDelete("/file", () =>
            DeleteFolder(Path.Combine(rootPath, "file"))).RequireAuthorization();

        app.MapMethods("/file/{fileName}", new string[] { "HEAD", "GET" }, async (string fileName) =>
        {
            if (InvalidFileName(fileName))
            {
                return Results.BadRequest();
            }
            return await GetFile(Path.Combine(rootPath, "file", fileName));
        }).RequireAuthorization();

        app.MapPut("/file/{fileName}", async (HttpContext content, string fileName) =>
        {
            if (InvalidFileName(fileName))
            {
                return Results.BadRequest();
            }
            return await PutFile(content, rootPath, Path.Combine(rootPath, "file", fileName));
        }).RequireAuthorization();

        app.MapGet("/SyncClipboard.json", () =>
            GetSyncProfile(rootPath, "SyncClipboard.json")).RequireAuthorization();

        app.MapPut("/SyncClipboard.json", ([FromBody] ClipboardProfileDTO profileDto) =>
            PutSyncProfile(profileDto, rootPath, "SyncClipboard.json")).RequireAuthorization();

        app.MapGet("/{name}", async (string name) =>
        {
            if (InvalidFileName(name))
            {
                return Results.BadRequest();
            }
            return await GetFile(Path.Combine(rootPath, name));
        }).RequireAuthorization();

        app.MapPut("/{name}", async (HttpContext content, string name) =>
        {
            if (InvalidFileName(name))
            {
                return Results.BadRequest();
            }
            return await PutFile(content, rootPath, Path.Combine(rootPath, name));
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