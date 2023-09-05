using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Server.Kestrel.Core;
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

        public static async Task<WebApplication> StartAsync(ushort prot, string path, string userName, string password)
        {
            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions
                {
                    WebRootPath = path + "server",
                }
            );
            builder.WebHost.UseUrls($"http://*:{prot}");
            builder.Services.AddSingleton<ICredentialChecker, StaticCredentialChecker>(_ => new StaticCredentialChecker(userName, password));
            var app = Configure(builder);
            app.UseSyncCliboardServer();
            await app.StartAsync();
            return app;
        }
    }
}