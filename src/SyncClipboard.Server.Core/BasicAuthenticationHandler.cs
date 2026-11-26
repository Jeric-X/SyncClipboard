using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SyncClipboard.Server.Core.Services;

namespace SyncClipboard.Server.Core;

public class BasicAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ServerSettingsStore checker
        ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var credentials = Encoding.UTF8.GetString(Convert.FromBase64String(header[6..])).Split(':', 2);
                if (credentials.Length == 2 && checker.Authenticate(credentials[0], credentials[1]) is string id)
                {
                    Claim[] claims = [new(ClaimTypes.NameIdentifier, id), new(ClaimTypes.Name, credentials[0])];
                    var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
                    return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
                }
            }
            catch (FormatException) { }
        }

        Response.Headers.WWWAuthenticate = "Basic realm=\"SyncClipboard\"";
        return Task.FromResult(AuthenticateResult.Fail("Invalid credentials"));
    }
}
