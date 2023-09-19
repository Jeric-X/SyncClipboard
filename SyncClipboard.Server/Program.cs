using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using SyncClipboard.Abstract;
using SyncClipboard.Server.Controller;

namespace SyncClipboard.Server
{
    public static class Program
    {
        private static WebApplication Configure(WebApplicationBuilder builder)
        {
            var services = builder.Services;

            services.Configure<KestrelServerOptions>(options => options.Limits.MaxRequestBodySize = int.MaxValue);

            services.AddAuthentication("BasicAuthentication")
                    .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>("BasicAuthentication", null);
            services.AddAuthorization(
                options => options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen();

            var app = builder.Build();

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseAuthentication();
            app.UseAuthorization();
            return app;
        }

        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions
                {
                    Args = args,
                    WebRootPath = "server"
                }
            );
            builder.Services.AddSingleton<ICredentialChecker, FileCredentialChecker>();
            var app = Configure(builder);
            app.UseSyncCliboardServer();
            app.Run();
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
            var app = Configure(builder);
            app.UseSyncCliboardServer(serverConfig);
            await app.StartAsync();
            return app;
        }
    }
}