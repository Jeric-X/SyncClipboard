using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using SyncClipboard.Abstract;
using SyncClipboard.Server.Core.Controller;
using SyncClipboard.Server.Core.CredentialChecker;
using System.Text.Json.Serialization;

namespace SyncClipboard.Server.Core;

public class Web
{
    public static WebApplication Configure(WebApplicationBuilder builder, bool useSwagger = false)
    {
        var services = builder.Services;

        services.Configure<KestrelServerOptions>(options => options.Limits.MaxRequestBodySize = int.MaxValue);

        services.AddAuthentication("BasicAuthentication")
                .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>("BasicAuthentication", null);
        services.AddAuthorizationBuilder().AddDefaultPolicy("DefaultPolicy", policy => policy.RequireAuthenticatedUser());

        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        // This is minimal api project, but Swagger use Microsoft.AspNetCore.Mvc.JsonOptions to show enum as string.
        // The real working converter is written in dto definition in form of attribute. 
        services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        var app = builder.Build();

        if (app.Environment.IsDevelopment() || useSwagger)
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }

    public static async Task<WebApplication> StartAsync(ServerPara serverConfig)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                WebRootPath = Path.Combine(serverConfig.Path, "server"),
            }
        );
        builder.WebHost.UseUrls($"http://*:{serverConfig.Port}");
        builder.Services.AddSingleton<ICredentialChecker, StaticCredentialChecker>(_ => new StaticCredentialChecker(serverConfig.UserName, serverConfig.Password));
        var app = Configure(builder, serverConfig.DiagnoseMode);
        app.UseSyncCliboardServer(serverConfig);
        await app.StartAsync();
        return app;
    }
}
