using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SyncClipboard.Server.Core;
using SyncClipboard.Server.Core.Models;

namespace SyncClipboard.Test;

[TestClass]
public class WebTests
{
    [TestMethod]
    public void ConfigureEmbeddedServerAppSettings_BindsFcmAndOverridesHistorySettings()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppSettings:EnableFcmPush"] = "true",
                ["AppSettings:FirebaseProjectId"] = "configured-project",
                ["AppSettings:MaxSavedHistoryCount"] = "1",
                ["AppSettings:HistoryRetentionMinutes"] = "2"
            })
            .Build();
        var services = new ServiceCollection();

        Web.ConfigureEmbeddedServerAppSettings(
            services,
            configuration,
            maxSavedHistoryCount: 300,
            historyRetentionMinutes: 400);

        using var provider = services.BuildServiceProvider();
        var settings = provider.GetRequiredService<IOptions<AppSettings>>().Value;
        Assert.IsTrue(settings.EnableFcmPush);
        Assert.AreEqual("configured-project", settings.FirebaseProjectId);
        Assert.AreEqual(300u, settings.MaxSavedHistoryCount);
        Assert.AreEqual(400u, settings.HistoryRetentionMinutes);
    }
}
