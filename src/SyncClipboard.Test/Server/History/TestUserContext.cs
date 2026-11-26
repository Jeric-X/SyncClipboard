using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace SyncClipboard.Test.Server.History;

internal static class TestUserContext
{
    public static DefaultHttpContext Create(string id = "default_user", CancellationToken token = default) => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], "Basic")),
        RequestAborted = token
    };
}
