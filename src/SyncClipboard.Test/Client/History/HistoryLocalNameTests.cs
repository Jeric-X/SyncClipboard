using Microsoft.EntityFrameworkCore;
using SyncClipboard.Core.Models;
using SyncClipboard.Shared.Profiles;

namespace SyncClipboard.Test.Client.History;

[TestClass]
public class HistoryLocalNameTests
{
    public TestContext TestContext { get; set; } = null!;
    private CancellationToken Token => TestContext.CancellationTokenSource.Token;

    [TestMethod]
    [DataRow(false, true, HistorySyncStatus.Synced, HistorySyncStatus.Synced)]
    [DataRow(true, false, HistorySyncStatus.Synced, HistorySyncStatus.Synced)]
    [DataRow(true, false, HistorySyncStatus.NeedSync, HistorySyncStatus.NeedSync)]
    [DataRow(true, true, HistorySyncStatus.Synced, HistorySyncStatus.NeedSync)]
    [DataRow(true, true, HistorySyncStatus.LocalOnly, HistorySyncStatus.LocalOnly)]
    public async Task EditAndClear_OnlyQueueChangesWhenNameSyncIsAvailable(
        bool syncEnabled, bool supportsName, HistorySyncStatus initialStatus, HistorySyncStatus expectedStatus)
    {
        await using var fixture = await HistoryRetentionTests.Fixture.CreateAsync(Token);
        fixture.RuntimeConfig.EnableSyncHistory = syncEnabled;
        var modified = DateTime.UtcNow.AddHours(-1);
        var record = new HistoryRecord
        {
            Type = ProfileType.Text,
            Hash = "ABC",
            Text = "clipboard content",
            SyncStatus = initialStatus,
            LastModified = modified,
            Version = 7
        };
        fixture.Db.Add(record);
        await fixture.Db.SaveChangesAsync(Token);
        var key = new HistoryRecordKey(record.Type, "abc");
        var notifications = 0;
        fixture.Manager.HistoryUpdated += _ => notifications++;

        Assert.IsFalse(await fixture.Manager.SetNameAsync(key, "", supportsName, Token));
        Assert.IsNull((await fixture.Db.HistoryRecords.AsNoTracking().SingleAsync(Token)).Name);
        Assert.IsTrue(await fixture.Manager.SetNameAsync(key, "  Release notes  ", supportsName, Token));
        var named = await fixture.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.AreEqual("Release notes", named.Name);
        Assert.AreEqual(expectedStatus, named.SyncStatus);
        Assert.AreEqual(8, named.Version);
        Assert.IsGreaterThan(modified, named.LastModified);

        Assert.IsFalse(await fixture.Manager.SetNameAsync(key, "Release notes ", supportsName, Token));
        var unchanged = await fixture.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.AreEqual(named.LastModified, unchanged.LastModified);
        Assert.AreEqual(named.Version, unchanged.Version);
        Assert.IsTrue(await fixture.Manager.SetNameAsync(key, "  ", supportsName, Token));
        var cleared = await fixture.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.AreEqual(string.Empty, cleared.Name);
        Assert.AreEqual(9, cleared.Version);
        Assert.AreEqual("clipboard content", cleared.Text);
        Assert.AreEqual(2, notifications);
    }

    [TestMethod]
    public async Task NameSearch_FindsDistinctRecordsAndKeepsRecopiedContent()
    {
        await using var fixture = await HistoryRetentionTests.Fixture.CreateAsync(Token);
        var manager = fixture.Manager;
        var profile = new TextProfile("original clipboard text");
        await manager.AddLocalProfile(profile, token: Token);
        var key = new HistoryRecordKey(profile.Type, await profile.GetHash(Token));
        var image = new HistoryRecord { Type = ProfileType.Image, Hash = key.Hash, Text = "icon.png", Stared = true };
        fixture.Db.Add(image);
        await fixture.Db.SaveChangesAsync(Token);

        await manager.SetNameAsync(key, "Checklist", false, Token);
        await manager.SetNameAsync(HistoryRecordKey.From(image), "Checklist", false, Token);
        await manager.AddLocalProfile(profile, token: Token);

        var contentOnly = new HistoryQueryPara { SearchText = "CHECKLIST" };
        Assert.IsEmpty(await manager.GetHistoryAsync(contentOnly, token: Token));
        var names = contentOnly with { SearchNames = true };
        var matches = await manager.GetHistoryAsync(names, token: Token);
        Assert.HasCount(2, matches);
        Assert.AreEqual(profile.DisplayText, matches.Single(r => r.Type == profile.Type).Text);
        var starred = await manager.GetHistoryAsync(names with { Starred = true }, minSize: 1, token: Token);
        Assert.AreEqual("icon.png", starred.Single().Text);
        Assert.AreEqual(key, HistoryRecordKey.From((await manager.GetHistoryAsync(
            names with { SearchText = "original" }, token: Token)).Single()));

        await manager.SetNameAsync(key, "", false, Token);
        Assert.AreEqual(image.ID, (await manager.GetHistoryAsync(names, token: Token)).Single().ID);
        await manager.DeleteHistory(image, Token);
        Assert.IsFalse(await manager.SetNameAsync(HistoryRecordKey.From(image), "deleted", false, Token));
        Assert.IsEmpty(await manager.GetHistoryAsync(names, token: Token));
    }

    [TestMethod]
    public async Task FailedRename_CanRetryTheSameValue()
    {
        await using var fixture = await HistoryRetentionTests.Fixture.CreateAsync(Token);
        fixture.RuntimeConfig.EnableSyncHistory = true;
        var record = new HistoryRecord { Type = ProfileType.Text, Hash = "ABC", SyncStatus = HistorySyncStatus.Synced };
        fixture.Db.Add(record);
        await fixture.Db.SaveChangesAsync(Token);
        var key = HistoryRecordKey.From(record);
        await fixture.Db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER fail_name BEFORE UPDATE OF Name ON HistoryRecords BEGIN SELECT RAISE(ABORT, 'write failed'); END;", Token);

        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => fixture.Manager.SetNameAsync(key, "note", true, Token));

        fixture.Db.Add(new HistoryRecord { Type = ProfileType.Text, Hash = "DEF" });
        await fixture.Db.SaveChangesAsync(Token);
        var unchanged = await fixture.Db.HistoryRecords.AsNoTracking().SingleAsync(r => r.Hash == key.Hash, Token);
        Assert.IsNull(unchanged.Name);
        Assert.AreEqual(HistorySyncStatus.Synced, unchanged.SyncStatus);
        await fixture.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_name", Token);
        Assert.IsTrue(await fixture.Manager.SetNameAsync(key, "note", true, Token));
        var saved = await fixture.Db.HistoryRecords.AsNoTracking().SingleAsync(r => r.Hash == key.Hash, Token);
        Assert.AreEqual("note", saved.Name);
        Assert.AreEqual(1, saved.Version);
        Assert.AreEqual(HistorySyncStatus.NeedSync, saved.SyncStatus);
    }

    [TestMethod]
    public async Task EditingAtMaximumVersion_LeavesAnUploadableVersion()
    {
        await using var fixture = await HistoryRetentionTests.Fixture.CreateAsync(Token);
        fixture.RuntimeConfig.EnableSyncHistory = true;
        var record = new HistoryRecord
        {
            Type = ProfileType.Text,
            Hash = "ABC",
            Version = int.MaxValue,
            SyncStatus = HistorySyncStatus.Synced
        };
        fixture.Db.Add(record);
        await fixture.Db.SaveChangesAsync(Token);

        await fixture.Manager.SetNameAsync(HistoryRecordKey.From(record), "note", true, Token);
        var saved = await fixture.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.AreEqual(int.MaxValue, saved.Version);
        Assert.AreEqual(HistorySyncStatus.NeedSync, saved.SyncStatus);
    }
}
