using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.StaticFiles;

namespace SyncClipboard.Server
{
    public static class Program
    {
        static IWebHostEnvironment? WebHostEnvironment;

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
            WebHostEnvironment = app.Environment;

            // Configure the HTTP request pipeline.
            //if (app.Environment.IsDevelopment())
            //{
            app.UseSwagger();
            app.UseSwaggerUI();
            //}

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
            Route(app);
            app.Run();
        }

        public static WebApplication Start(short prot, string path, string userName, string password)
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
            Route(app);
            app.StartAsync();
            return app;
        }

        private static async Task<IResult> PutFile(HttpContext content, string path)
        {
            var pathFolder = Path.Combine(WebHostEnvironment?.WebRootPath!, "file");
            if (!Directory.Exists(pathFolder))
            {
                Directory.CreateDirectory(pathFolder);
            }
            using var fs = new FileStream(path, FileMode.Create);
            await content.Request.Body.CopyToAsync(fs);
            return Results.Ok();
        }

        private static IResult GetFile(string path)
        {
            if (!File.Exists(path))
            {
                return Results.NotFound();
            }
            new FileExtensionContentTypeProvider().TryGetContentType(path, out string? contentType);
            return Results.File(path, contentType);
        }

        private static void Route(WebApplication app)
        {
            //app.UseStaticFiles();
            app.MapMethods("/file", new string[] { "HEAD" }, () =>
            {
                var path = Path.Combine(WebHostEnvironment?.WebRootPath!, "file");
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                return Results.Ok();
            }).RequireAuthorization();

            app.MapDelete("/file", () =>
            {
                var path = Path.Combine(WebHostEnvironment?.WebRootPath!, "file");
                if (Directory.Exists(path))
                {
                    Directory.Delete(Path.Combine(WebHostEnvironment?.WebRootPath!, "file"), true);
                    return Results.Ok();
                }
                return Results.NotFound();
            }).RequireAuthorization();

            app.MapGet("/file/{fileName}", (string fileName) =>
                GetFile(Path.Combine(WebHostEnvironment?.WebRootPath!, "file", fileName))).RequireAuthorization();

            app.MapPut("/file/{fileName}", (HttpContext content, string fileName) =>
                PutFile(content, Path.Combine(WebHostEnvironment?.WebRootPath!, "file", fileName))).RequireAuthorization();

            app.MapGet("/{name}", (string name) =>
                GetFile(Path.Combine(WebHostEnvironment?.WebRootPath!, name))).RequireAuthorization();

            app.MapPut("/{name}", (HttpContext content, string name) =>
                PutFile(content, Path.Combine(WebHostEnvironment?.WebRootPath!, name))).RequireAuthorization();
        }
    }
}