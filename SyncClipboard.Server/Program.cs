using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.StaticFiles;

namespace SyncClipboard.Server
{
    public static class Program
    {
        static IWebHostEnvironment? WebHostEnvironment;

        private static WebApplication Configure(WebApplicationBuilder builder)
        {
            // Add services to the container.
            builder.Services.AddAuthentication("BasicAuthentication")
                            .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>
                            ("BasicAuthentication", null);
            builder.Services.AddAuthorization(
                options => options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

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
            var builder = WebApplication.CreateBuilder(args);
            var app = Configure(builder);
            app.Run();
        }

        public static WebApplication Start(short prot, string path)
        {
            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions
                {
                    WebRootPath = path + "wwwroot"
                }
            );
            builder.WebHost.UseUrls($"http://*:{prot}");
            var app = Configure(builder);
            Route(app);
            app.StartAsync();
            return app;
        }

        private static void Route(WebApplication app)
        {
            //app.UseStaticFiles();
            app.MapMethods("/file", new string[] { "PROPFIND" }, () =>
            {
                var path = Path.Combine(WebHostEnvironment?.WebRootPath!, "file");
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                return Results.Ok();
            }).RequireAuthorization();

            app.MapPut("/file/{fileName}", async (HttpContext content, string fileName) =>
            {
                var path = Path.Combine(WebHostEnvironment?.WebRootPath!, "file", fileName);
                using var fs = new FileStream(path, FileMode.Create);
                await content.Request.Body.CopyToAsync(fs);
                return Results.Ok();
            }).RequireAuthorization();

            app.MapGet("/file/{fileName}", (string fileName) =>
            {
                var path = Path.Combine(WebHostEnvironment?.WebRootPath!, "file", fileName);
                if (!File.Exists(path))
                {
                    return Results.NotFound();
                }
                return Results.File(path);
            }).RequireAuthorization();

            app.MapGet("/{name}", (string name) =>
            {
                var path = Path.Combine(WebHostEnvironment?.WebRootPath!, name);
                if (!File.Exists(path))
                {
                    return Results.NotFound();
                }
                return Results.File(path);
            }).RequireAuthorization();

            app.MapPut("/{name}", async (HttpContext content, string name) =>
            {
                using var fs = new FileStream(Path.Combine(WebHostEnvironment?.WebRootPath!, name), FileMode.Create);
                await content.Request.Body.CopyToAsync(fs);
                return Results.Ok();
            }).RequireAuthorization();
        }
    }
}