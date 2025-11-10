using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using SyncClipboard.Server.Core.Controllers;
using SyncClipboard.Server.Core.CredentialChecker;
using SyncClipboard.Server.Core.Hubs;
using System.Net;
using System.Text.Json.Serialization;
using SyncClipboard.Server.Core.Services.History;
using SyncClipboard.Server.Core.Utilities.History;
using SyncClipboard.Server.Core.Utilities;

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

        services.AddControllers()
            .AddApplicationPart(typeof(SyncClipboardController).Assembly);
        services.AddMemoryCache();
        services.AddSignalR();

        services.AddDbContext<HistoryDbContext>();
        services.AddScoped<HistoryService>();

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

        MigrationHelper.EnsureDBMigrations(app.Services, app.Lifetime);

        if (app.Environment.IsDevelopment() || useSwagger)
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapHub<SyncClipboardHub>(Constants.SignalRConstants.HubPath);

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

        if (serverConfig.EnableCustomConfigurationFile)
        {
            var configFile = serverConfig.CustomConfigurationFilePath;
            if (string.IsNullOrEmpty(configFile))
            {
                throw new ArgumentException("CustomConfigurationFilePath is empty");
            }
            builder.Configuration.AddJsonFile(configFile, optional: false, reloadOnChange: true);
        }
        else
        {
            if (serverConfig.EnableHttps)
            {
                var dict = new Dictionary<string, string?>
                {
                    {"Kestrel:Certificates:Default:KeyPath", serverConfig.CertificatePemKeyPath},
                    {"Kestrel:Certificates:Default:Path", serverConfig.CertificatePemPath}
                };
                builder.Configuration.AddInMemoryCollection(dict);
            }

            builder.WebHost.UseKestrel((context, serverOptions) =>
            {
                serverOptions.Listen(IPAddress.Any, serverConfig.Port, listenOptions =>
                {
                    if (serverConfig.EnableHttps)
                    {
                        listenOptions.UseHttps();
                    }
                });
            });
        }
        builder.Services.AddSingleton<ICredentialChecker, StaticCredentialChecker>(_ => new StaticCredentialChecker(serverConfig.UserName, serverConfig.Password));
        var app = Configure(builder, serverConfig.DiagnoseMode);
        await app.StartAsync();
        return app;
    }
}
