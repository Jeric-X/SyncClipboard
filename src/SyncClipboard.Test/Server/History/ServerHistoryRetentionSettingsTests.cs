using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using SyncClipboard.Server.Core.Hubs;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Server.Core.Services.History;
using SyncClipboard.Server.Core.Utilities.History;
using SyncClipboard.Shared;
using SyncClipboard.Shared.Profiles;

namespace SyncClipboard.Test.Server.History;

[TestClass]
public class ServerHistoryRetentionSettingsTests
{
    private static readonly string[] RecordHashes = ["UNTAGGED", "UNKNOWN", "TAGGED", "STARRED", "PINNED", "ALREADY-DELETED", "TITLED", "EMPTY-TITLE"];

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(null, null, false)]
    [DataRow(null, null, true)]
    [DataRow(false, true, false)]
    [DataRow(false, true, true)]
    [DataRow(true, false, false)]
    [DataRow(true, false, true)]
    public async Task Cleanup_RespectsIndependentProtectionAndBroadcastsDeletedRecords(
        bool? protectTagged, bool? protectTitled, bool countLimit)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationTokenSource.Token);
        cts.CancelAfter(TimeSpan.FromMinutes(1));
        var token = cts.Token;
        await using var db = new HistoryDbContext();
        db.Database.SetConnectionString("Data Source=:memory:");
        await db.Database.OpenConnectionAsync(token);
        await db.Database.MigrateAsync(token);
        var root = Directory.CreateTempSubdirectory("server-tag-cleanup-");
        try
        {
            var env = new Mock<IProfileEnv>();
            env.Setup(x => x.GetPersistentDir()).Returns(root.FullName);
            var client = new Mock<ISyncClipboardClient>();
            var notifications = new List<HistoryRecordDto>();
            client.Setup(x => x.RemoteHistoryChanged(It.IsAny<HistoryRecordDto>()))
                .Callback<HistoryRecordDto>(notifications.Add).Returns(Task.CompletedTask);
            var hub = new Mock<IHubContext<SyncClipboardHub, ISyncClipboardClient>>();
            hub.Setup(x => x.Clients.User("default_user")).Returns(client.Object);
            var settings = new AppSettings { HistoryRetentionMinutes = 60 };
            if (protectTagged.HasValue)
                settings.ProtectTaggedRecords = protectTagged.Value;
            if (protectTitled.HasValue)
                settings.ProtectTitledRecords = protectTitled.Value;
            var options = Options.Create(settings);
            var service = new HistoryService(db, env.Object, hub.Object, options);
            var records = CreateRecords(countLimit);
            db.AddRange(records);
            await db.SaveChangesAsync(token);

            if (countLimit)
                await service.SetRecordsMaxCount(1, token);
            else
                await service.RemoveOutOfRetentionRecords(token);

            var saved = await db.HistoryRecords.AsNoTracking().ToDictionaryAsync(r => r.Hash, token);
            Assert.IsTrue(saved["UNTAGGED"].IsDeleted);
            Assert.IsTrue(saved["UNKNOWN"].IsDeleted);
            Assert.IsTrue(saved["EMPTY-TITLE"].IsDeleted);
            Assert.AreEqual(protectTitled == false, saved["TITLED"].IsDeleted);
            Assert.AreEqual(protectTagged == false, saved["TAGGED"].IsDeleted);
            Assert.IsFalse(saved["STARRED"].IsDeleted);
            Assert.IsFalse(saved["PINNED"].IsDeleted);
            Assert.AreEqual(0, saved["ALREADY-DELETED"].Version);
            if (!countLimit)
            {
                Assert.IsFalse(saved["RECENT-ACCESS"].IsDeleted);
                Assert.IsFalse(saved["RECENT-MODIFIED"].IsDeleted);
            }

            List<string> expected = ["UNTAGGED", "UNKNOWN", "EMPTY-TITLE"];
            if (protectTagged == false)
                expected.Add("TAGGED");
            if (protectTitled == false)
                expected.Add("TITLED");
            CollectionAssert.AreEquivalent(expected, notifications.Select(r => r.Hash).ToArray());
            foreach (var notification in notifications)
            {
                Assert.IsTrue(notification.IsDeleted);
                Assert.AreEqual(1, notification.Version);
                Assert.AreEqual(1, saved[notification.Hash].Version);
                CollectionAssert.AreEqual(saved[notification.Hash].Tags, notification.Tags!);
                Assert.AreEqual(saved[notification.Hash].Title, notification.Title);
            }
        }
        finally
        {
            root.Delete(true);
        }
    }

    private static List<HistoryRecordEntity> CreateRecords(bool countLimit)
    {
        var old = DateTime.UtcNow.AddDays(-10);
        var records = RecordHashes.Select(hash => new HistoryRecordEntity
        {
            UserId = "default_user",
            Type = ProfileType.Text,
            Hash = hash,
            Tags = hash == "UNKNOWN" ? null : hash is "UNTAGGED" or "TITLED" or "EMPTY-TITLE" ? [] : ["work"],
            Title = hash == "EMPTY-TITLE" ? string.Empty : hash is "TITLED" or "STARRED" or "PINNED" ? "Keep" : null,
            LastModified = old,
            LastAccessed = old,
            Stared = hash == "STARRED",
            Pinned = hash == "PINNED",
            IsDeleted = hash == "ALREADY-DELETED"
        }).ToList();
        if (!countLimit)
        {
            records.Add(new HistoryRecordEntity
            {
                UserId = "default_user",
                Type = ProfileType.Text,
                Hash = "RECENT-ACCESS",
                Tags = ["work"],
                LastModified = old,
                LastAccessed = DateTime.UtcNow
            });
            records.Add(new HistoryRecordEntity
            {
                UserId = "default_user",
                Type = ProfileType.Text,
                Hash = "RECENT-MODIFIED",
                Tags = ["work"],
                LastModified = DateTime.UtcNow,
                LastAccessed = old
            });
        }
        return records;
    }
}
