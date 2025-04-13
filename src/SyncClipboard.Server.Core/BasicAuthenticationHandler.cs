using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SyncClipboard.Server.Core.CredentialChecker;

namespace SyncClipboard.Server.Core;

public class BasicAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ICredentialChecker checker
        ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authHeader = Request.Headers.Authorization.ToString();
        if (authHeader?.StartsWith("basic", StringComparison.OrdinalIgnoreCase) == true)
        {
            var token = authHeader["Basic ".Length..].Trim();
            var credentialstring = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var credentials = credentialstring.Split(':');
            if (checker.Check(credentials[0], credentials[1]))
            {
                var claims = new[] { new Claim("name", credentials[0]), new Claim(ClaimTypes.Role, "Admin") };
                var identity = new ClaimsIdentity(claims, "Basic");
                var claimsPrincipal = new ClaimsPrincipal(identity);
                return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(claimsPrincipal, Scheme.Name)));
            }

            Response.StatusCode = 401;
            Response.Headers.WWWAuthenticate = "Basic realm=\"SyncClipboard\"";
            return Task.FromResult(AuthenticateResult.Fail("Invalid Authorization Header"));
        }
        else
        {
            Response.StatusCode = 401;
            Response.Headers.WWWAuthenticate = "Basic realm=\"SyncClipboard\"";
            return Task.FromResult(AuthenticateResult.Fail("Invalid Authorization Header"));
        }
    }
}