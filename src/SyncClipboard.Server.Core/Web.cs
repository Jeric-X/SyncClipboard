using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using SyncClipboard.Server.Core.Controllers;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using SyncClipboard.Server.Core.Hubs;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Server.Core.Services;
using SyncClipboard.Server.Core.Services.History;
using SyncClipboard.Server.Core.Swagger;
using SyncClipboard.Server.Core.Utilities;
using SyncClipboard.Server.Core.Utilities.History;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace SyncClipboard.Server.Core;

public class Web
{
    public static WebApplication Configure(WebApplicationBuilder builder, ServerSettingsStore settings, bool useSwagger = false)
    {
        var services = builder.Services;

        services.AddSingleton(settings);
        services.AddSingleton<IOptions<AppSettings>>(settings);
        services.Configure<KestrelServerOptions>(options => options.Limits.MaxRequestBodySize = int.MaxValue);

        services.AddAuthentication("BasicAuthentication")
            .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>("BasicAuthentication", null);
        services.AddAuthorizationBuilder().AddDefaultPolicy("DefaultPolicy", policy => policy.RequireAuthenticatedUser())
            .AddPolicy("ServerAdministrator", policy => policy.RequireClaim(ClaimTypes.NameIdentifier, ServerSettingsStore.AdministratorId));

        services.AddControllers()
            .AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
            })
            .AddApplicationPart(typeof(SyncClipboardController).Assembly);
        services.AddSignalR();

        services.AddDbContext<HistoryDbContext>();
        services.AddScoped<HistoryService>();

        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.OperationFilter<MultipartFormDataOperationFilter>();
            options.OperationFilter<QueryHistoryOperationFilter>();
        });

        services.AddServerProfileEnvProvider();
        services.AddHostedService<HistoryCleaner>();

        // This is minimal api project, but Swagger use Microsoft.AspNetCore.Mvc.JsonOptions to show enum as string.
        // The real working converter is written in dto definition in form of attribute. 
        services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });
        var app = builder.Build();

        app.Services.GetRequiredService<ServerEnvProvider>().MigrateLegacyClipboard();
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
                ContentRootPath = serverConfig.Path,
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
                void OptionAction(ListenOptions options)
                {
                    if (serverConfig.EnableHttps)
                    {
                        options.UseHttps();
                    }
                }
                serverOptions.ListenAnyIP(serverConfig.Port, OptionAction);
            });
        }
        var settings = new ServerSettingsStore(serverConfig.Path, new(), null);
        var app = Configure(builder, settings, serverConfig.DiagnoseMode);
        await app.StartAsync();
        return app;
    }
}
