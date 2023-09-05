using Microsoft.AspNetCore.StaticFiles;

namespace SyncClipboard.Server.Controller;

public static class SyncClipboardController
{
    public static void UseSyncCliboardServer(this WebApplication webApplication, bool passive = false)
    {
        webApplication.Route(passive);
    }

    private static async Task<IResult> PutFile(HttpContext content, string rootPath, string path)
    {
        var pathFolder = Path.Combine(rootPath, "file");
        if (!Directory.Exists(pathFolder))
        {
            Directory.CreateDirectory(pathFolder);
        }
        using var fs = new FileStream(path, FileMode.Create);
        await content.Request.Body.CopyToAsync(fs);
        return Results.Ok();
    }

    private static IResult GetFile(string path)
    {
        if (!File.Exists(path))
        {
            return Results.NotFound();
        }
        new FileExtensionContentTypeProvider().TryGetContentType(path, out string? contentType);
        return Results.File(path, contentType);
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
            Directory.Delete(path, true);
            return Results.Ok();
        }
        return Results.NotFound();
    }

    private static IResult GetSyncProfile(string path, bool passive)
    {
        return GetFile(path);
    }

    private static async Task<IResult> PutSyncProfile(HttpContext content, string path, bool passive)
    {
        using var fs = new FileStream(path, FileMode.Create);
        await content.Request.Body.CopyToAsync(fs);
        return Results.Ok();
    }

    private static void Route(this WebApplication app, bool passive)
    {
        var rootPath = app.Environment.WebRootPath;
        app.MapMethods("/file", new string[] { "HEAD" }, () =>
            ExistOrCreateFolder(Path.Combine(rootPath, "file"))).RequireAuthorization();

        app.MapDelete("/file", () =>
            DeleteFolder(Path.Combine(rootPath, "file"))).RequireAuthorization();

        app.MapGet("/file/{fileName}", (string fileName) =>
            GetFile(Path.Combine(rootPath, "file", fileName))).RequireAuthorization();

        app.MapPut("/file/{fileName}", (HttpContext content, string fileName) =>
            PutFile(content, rootPath, Path.Combine(rootPath, "file", fileName))).RequireAuthorization();

        app.MapGet("/SyncClipboard.json", () =>
            GetSyncProfile(Path.Combine(rootPath, "SyncClipboard.json"), passive)).RequireAuthorization();

        app.MapPut("/SyncClipboard.json", (HttpContext content) =>
            PutSyncProfile(content, Path.Combine(rootPath, "SyncClipboard.json"), passive)).RequireAuthorization();

        app.MapGet("/{name}", (string name) =>
            GetFile(Path.Combine(rootPath, name))).RequireAuthorization();

        app.MapPut("/{name}", (HttpContext content, string name) =>
            PutFile(content, rootPath, Path.Combine(rootPath, name))).RequireAuthorization();
    }
}
