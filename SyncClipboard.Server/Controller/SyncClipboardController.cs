using Microsoft.AspNetCore.StaticFiles;

namespace SyncClipboard.Server.Controller;

public class SyncClipboardController
{
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
            Directory.Delete(path, true);
            return Results.Ok();
        }
        return Results.NotFound();
    }

    protected virtual Task<IResult> GetSyncProfile(string rootPath, string path)
    {
        return GetFile(Path.Combine(rootPath, path));
    }

    protected virtual async Task<IResult> PutSyncProfile(HttpContext content, string rootPath, string path)
    {
        using var fs = new FileStream(Path.Combine(rootPath, path), FileMode.Create);
        await content.Request.Body.CopyToAsync(fs);
        return Results.Ok();
    }

    public void Route(WebApplication app)
    {
        var rootPath = app.Environment.WebRootPath;
        app.MapMethods("/file", new string[] { "HEAD", "MKCOL" }, () =>
            ExistOrCreateFolder(Path.Combine(rootPath, "file"))).RequireAuthorization();

        app.MapDelete("/file", () =>
            DeleteFolder(Path.Combine(rootPath, "file"))).RequireAuthorization();

        app.MapGet("/file/{fileName}", (string fileName) =>
            GetFile(Path.Combine(rootPath, "file", fileName))).RequireAuthorization();

        app.MapPut("/file/{fileName}", (HttpContext content, string fileName) =>
            PutFile(content, rootPath, Path.Combine(rootPath, "file", fileName))).RequireAuthorization();

        app.MapGet("/SyncClipboard.json", () =>
            GetSyncProfile(rootPath, "SyncClipboard.json")).RequireAuthorization();

        app.MapPut("/SyncClipboard.json", (HttpContext content) =>
            PutSyncProfile(content, rootPath, "SyncClipboard.json")).RequireAuthorization();

        app.MapGet("/{name}", (string name) =>
            GetFile(Path.Combine(rootPath, name))).RequireAuthorization();

        app.MapPut("/{name}", (HttpContext content, string name) =>
            PutFile(content, rootPath, Path.Combine(rootPath, name))).RequireAuthorization();
    }
}
