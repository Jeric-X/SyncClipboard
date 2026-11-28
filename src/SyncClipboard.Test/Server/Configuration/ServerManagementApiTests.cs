using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.SignalR.Client;
using SyncClipboard.Server.Core.Constants;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using SyncClipboard.Server.Core;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Server.Core.Services;
using SyncClipboard.Shared;

namespace SyncClipboard.Test.Server.Configuration;

[TestClass]
public class ServerManagementApiTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task EmbeddedHostAppliesAdministratorChangesImmediatelyAndPersistsThem()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var configPath = Path.Combine(root.FullName, "host.json");
            await File.WriteAllTextAsync(configPath, """{"Urls":"http://127.0.0.1:0"}""", token);
            _ = new ServerSettingsStore(root.FullName, new(), new("alice", "original"));
            var host = new ServerPara(0, root.FullName, false, "", "", true, configPath, false);
            await using var app = await Web.StartAsync(host);
            try
            {
                using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
                client.DefaultRequestHeaders.Authorization = Login("alice", "original");
                using var add = await client.PostAsJsonAsync("api/admin/users", new AccountUpdate("bob", "second"), token);
                add.EnsureSuccessStatusCode();
                var accounts = await client.GetFromJsonAsync<AccountSummary[]>("api/admin/users", token);
                var bob = accounts!.Single(a => a.UserName == "bob");

                client.DefaultRequestHeaders.Authorization = Login("bob", "second");
                using var denied = await client.GetAsync("api/admin/settings", token);
                Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
                client.DefaultRequestHeaders.Authorization = Login("alice", "original");
                using var change = await client.PutAsJsonAsync("api/admin/settings", new AppSettings { MaxSavedHistoryCount = 17 }, token);
                change.EnsureSuccessStatusCode();
                var saved = await client.GetFromJsonAsync<AppSettings>("api/admin/settings", token);
                Assert.AreEqual(17u, saved!.MaxSavedHistoryCount);
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await using var hub = new HubConnectionBuilder().WithUrl(new Uri(client.BaseAddress, SignalRConstants.HubPath),
                    options => options.Headers["Authorization"] = Login("bob", "second").ToString()).Build();
                hub.Closed += _ =>
                {
                    closed.TrySetResult();
                    return Task.CompletedTask;
                };
                await hub.StartAsync(token);
                using var reset = await client.PutAsJsonAsync($"api/admin/users/{bob.Id}", new AccountUpdate("bob", "third"), token);
                reset.EnsureSuccessStatusCode();
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
                client.DefaultRequestHeaders.Authorization = Login("bob", "third");
                using var accepted = await client.GetAsync("api/time", token);
                accepted.EnsureSuccessStatusCode();
                client.DefaultRequestHeaders.Authorization = Login("alice", "original");
                using var remove = await client.DeleteAsync($"api/admin/users/{bob.Id}", token);
                remove.EnsureSuccessStatusCode();
                client.DefaultRequestHeaders.Authorization = Login("bob", "third");
                using var revoked = await client.GetAsync("api/time", token);
                Assert.AreEqual(HttpStatusCode.Unauthorized, revoked.StatusCode);
            }
            finally
            {
                await app.StopAsync(token);
            }

            var restarted = new ServerSettingsStore(root.FullName, new(), null);
            Assert.AreEqual(17u, restarted.Value.MaxSavedHistoryCount);
            Assert.IsNull(restarted.Authenticate("bob", "third"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            root.Delete(true);
        }
    }

    private static AuthenticationHeaderValue Login(string user, string password) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));
}
