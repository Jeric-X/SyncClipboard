using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using SyncClipboard.Server.Core.Controllers;
using SyncClipboard.Server.Core.CredentialChecker;
using SyncClipboard.Server.Core.Hubs;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Server.Core.Services;
using SyncClipboard.Server.Core.Services.History;
using SyncClipboard.Server.Core.Services.Notifications;
using SyncClipboard.Server.Core.Services.Notifications.Fcm;
using SyncClipboard.Server.Core.Services.PushDevices;
using SyncClipboard.Server.Core.Swagger;
using SyncClipboard.Server.Core.Utilities;
using SyncClipboard.Server.Core.Utilities.History;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Unicode;

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
            .AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
            })
            .AddApplicationPart(typeof(SyncClipboardController).Assembly);
        services.AddMemoryCache();
        services.AddSignalR();
        services.AddSingleton<SignalRProfileChangeNotifier>();
        services.AddSingleton<IFcmPushClient, FirebaseAdminFcmPushClient>();
        services.AddSingleton<IFcmProfileChangeQueue, FcmProfileChangeQueue>();
        services.AddScoped<FcmProfileChangeDelivery>();
        services.AddScoped<FcmProfileChangeNotifier>();
        services.AddScoped<IProfileChangeNotifier>(provider =>
            new CompositeProfileChangeNotifier([
                provider.GetRequiredService<SignalRProfileChangeNotifier>(),
                provider.GetRequiredService<FcmProfileChangeNotifier>()
            ]));

        services.AddDbContext<HistoryDbContext>();
        services.AddScoped<HistoryService>();
        services.AddScoped<IPushDeviceRegistry, PushDeviceRegistry>();

        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.OperationFilter<MultipartFormDataOperationFilter>();
            options.OperationFilter<QueryHistoryOperationFilter>();
        });

        services.AddServerProfileEnvProvider();
        services.AddHostedService<HistoryCleaner>();
        services.AddHostedService<FcmProfileChangeWorker>();

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
        ConfigureEmbeddedServerAppSettings(
            builder.Services,
            builder.Configuration,
            serverConfig.MaxSavedHistoryCount,
            serverConfig.HistoryRetentionMinutes);
        builder.Services.AddSingleton<ICredentialChecker, StaticCredentialChecker>(_ => new StaticCredentialChecker(serverConfig.UserName, serverConfig.Password));
        var app = Configure(builder, serverConfig.DiagnoseMode);
        await app.StartAsync();
        return app;
    }

    internal static void ConfigureEmbeddedServerAppSettings(
        IServiceCollection services,
        IConfiguration configuration,
        uint maxSavedHistoryCount,
        uint historyRetentionMinutes)
    {
        services.Configure<AppSettings>(configuration.GetSection(nameof(AppSettings)));
        services.PostConfigure<AppSettings>(option =>
        {
            option.MaxSavedHistoryCount = maxSavedHistoryCount;
            option.HistoryRetentionMinutes = historyRetentionMinutes;
        });
    }
}
