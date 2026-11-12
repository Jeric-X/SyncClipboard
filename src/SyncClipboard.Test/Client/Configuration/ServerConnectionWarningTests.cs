using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NativeNotification.Interface;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer;
using SyncClipboard.Core.RemoteServer.Adapter;
using SyncClipboard.Core.RemoteServer.Adapter.OfficialServer;
using SyncClipboard.Core.ViewModels;
using ILogger = SyncClipboard.Core.Interfaces.ILogger;

namespace SyncClipboard.Test.Client.Configuration;

[TestClass]
public class ServerConnectionWarningTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("{\"historySync\":true}", true, true)]
    [DataRow("{\"historySync\":true,\"historyTags\":true}", true, false)]
    [DataRow("{\"historySync\":false}", false, false)]
    [DataRow(null, true, true)]
    [DataRow("invalid json", false, false)]
    public async Task TestConfiguration_ReportsWarningOrErrorWithoutSystemNotification(
        string? capabilities, bool success, bool warning)
    {
        await using var fixture = await Fixture.CreateAsync(TestContext.CancellationTokenSource.Token);
        fixture.Capabilities = capabilities;

        await fixture.ViewModel.TestConnectionCommand.ExecuteAsync(null);

        Assert.AreEqual(success, fixture.ViewModel.TestResult, fixture.ViewModel.ErrorMessage);
        Assert.AreEqual(!success, fixture.ViewModel.HasError);
        Assert.AreEqual(warning, fixture.ViewModel.HasWarning);
        if (warning)
            Assert.AreEqual(Strings.HistoryTagsUpgrade, fixture.ViewModel.WarningMessage);
        Assert.AreEqual(1, fixture.CapabilityRequests);
        Assert.AreEqual(capabilities is null ? 1 : 0, fixture.VersionRequests);
        Assert.IsEmpty(fixture.Notifications.Invocations);
    }

    [TestMethod]
    public async Task Retest_ClearsPreviousWarningWhenServerGainsTagSupport()
    {
        await using var fixture = await Fixture.CreateAsync(TestContext.CancellationTokenSource.Token);
        await fixture.ViewModel.TestConnectionCommand.ExecuteAsync(null);
        Assert.IsTrue(fixture.ViewModel.HasWarning, fixture.ViewModel.ErrorMessage);

        fixture.Capabilities = "{\"historySync\":true,\"historyTags\":true}";
        await fixture.ViewModel.TestConnectionCommand.ExecuteAsync(null);

        Assert.IsTrue(fixture.ViewModel.IsTestSuccess);
        Assert.IsFalse(fixture.ViewModel.HasError);
        Assert.IsFalse(fixture.ViewModel.HasWarning);
        Assert.IsEmpty(fixture.Notifications.Invocations);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"connection-warning-{Guid.NewGuid():N}");
        private readonly ConfigurationTestServices _configurationServices = new();
        private readonly WebApplication _app;
        private ServiceProvider _services = null!;
        private RemoteClipboardServerFactory _factory = null!;
        public string? Capabilities { get; set; } = "{\"historySync\":true}";
        public int CapabilityRequests { get; private set; }
        public int VersionRequests { get; private set; }
        public Mock<INotificationManager> Notifications { get; } = new();
        public AccountConfigEditViewModel ViewModel { get; private set; } = null!;

        private Fixture()
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            _app = builder.Build();
            _app.Run(async context =>
            {
                if (context.Request.Path == "/api/capabilities")
                {
                    CapabilityRequests++;
                    if (Capabilities is null)
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                    else
                    {
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(Capabilities, context.RequestAborted);
                    }
                }
                else if (context.Request.Path == "/api/version")
                {
                    VersionRequests++;
                    await context.Response.WriteAsync("3.1.1", context.RequestAborted);
                }
                else
                    context.Response.StatusCode = context.Request.Method == "PROPFIND"
                        ? StatusCodes.Status207MultiStatus : StatusCodes.Status404NotFound;
            });
        }

        public static async Task<Fixture> CreateAsync(CancellationToken token)
        {
            var fixture = new Fixture();
            await fixture._app.StartAsync(token);
            Directory.CreateDirectory(fixture._directory);
            var path = Path.Combine(fixture._directory, "SyncClipboard.json");
            File.WriteAllText(path, """{"ConfigVersion":1}""");
            var config = new ConfigManager(path, fixture._configurationServices.Upgrader);
            var appConfig = Mock.Of<IAppConfig>(c => c.AppStringId == "SyncClipboardTests/" && c.AppVersion == "1.0");
            fixture._services = new ServiceCollection()
                .AddSingleton(config)
                .AddSingleton<AccountManager>()
                .AddSingleton(Mock.Of<ILogger>())
                .AddKeyedSingleton<ServerAdapterFactory>(OfficialConfig.ConfigTypeName, (_, _) =>
                    _ => new OfficialAdapter(Mock.Of<ILogger>(), appConfig, fixture.Notifications.Object))
                .BuildServiceProvider();
            fixture._factory = new RemoteClipboardServerFactory(fixture._services);
            fixture.ViewModel = new AccountConfigEditViewModel(
                fixture._services.GetRequiredService<AccountManager>(), fixture._factory, config, null!);
            fixture.ViewModel.LoadProperties(new AccountConfig { AccountType = OfficialConfig.ConfigTypeName });
            fixture.ViewModel.Properties.Single(p => p.PropertyName == nameof(OfficialConfig.RemoteURL)).Value = fixture._app.Urls.Single();
            return fixture;
        }

        public async ValueTask DisposeAsync()
        {
            _factory.Dispose();
            await _services.DisposeAsync();
            _configurationServices.Dispose();
            await _app.DisposeAsync();
            Directory.Delete(_directory, recursive: true);
        }
    }
}
