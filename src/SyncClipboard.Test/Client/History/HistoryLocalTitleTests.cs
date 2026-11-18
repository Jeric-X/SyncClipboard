using Microsoft.EntityFrameworkCore;
using SyncClipboard.Core.Models;
using SyncClipboard.Shared.Profiles;

namespace SyncClipboard.Test.Client.History;

[TestClass]
public class HistoryLocalTitleTests
{
    public TestContext TestContext { get; set; } = null!;
    private CancellationToken Token => TestContext.CancellationTokenSource.Token;

    [TestMethod]
    [DataRow(false, true, HistorySyncStatus.Synced, HistorySyncStatus.Synced)]
    [DataRow(true, false, HistorySyncStatus.Synced, HistorySyncStatus.Synced)]
    [DataRow(true, false, HistorySyncStatus.NeedSync, HistorySyncStatus.NeedSync)]
    [DataRow(true, true, HistorySyncStatus.Synced, HistorySyncStatus.NeedSync)]
    [DataRow(true, true, HistorySyncStatus.LocalOnly, HistorySyncStatus.LocalOnly)]
    public async Task EditAndClear_OnlyQueueChangesWhenTitleSyncIsAvailable(
        bool syncEnabled, bool supportsTitle, HistorySyncStatus initialStatus, HistorySyncStatus expectedStatus)
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

        Assert.IsFalse(await fixture.Manager.SetTitleAsync(key, "", supportsTitle, Token));
        Assert.IsNull((await fixture.Db.HistoryRecords.AsNoTracking().SingleAsync(Token)).Title);
        Assert.IsTrue(await fixture.Manager.SetTitleAsync(key, "  Release notes  ", supportsTitle, Token));
        var titled = await fixture.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.AreEqual("Release notes", titled.Title);
        Assert.AreEqual(expectedStatus, titled.SyncStatus);
        Assert.AreEqual(8, titled.Version);
        Assert.IsGreaterThan(modified, titled.LastModified);

        Assert.IsFalse(await fixture.Manager.SetTitleAsync(key, "Release notes ", supportsTitle, Token));
        var unchanged = await fixture.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.AreEqual(titled.LastModified, unchanged.LastModified);
        Assert.AreEqual(titled.Version, unchanged.Version);
        Assert.IsTrue(await fixture.Manager.SetTitleAsync(key, "  ", supportsTitle, Token));
        var cleared = await fixture.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.AreEqual(string.Empty, cleared.Title);
        Assert.AreEqual(9, cleared.Version);
        Assert.AreEqual("clipboard content", cleared.Text);
        Assert.AreEqual(2, notifications);
    }

    [TestMethod]
    public async Task TitleSearch_FindsDistinctRecordsAndKeepsRecopiedContent()
    {
        await using var fixture = await HistoryRetentionTests.Fixture.CreateAsync(Token);
        var manager = fixture.Manager;
        var profile = new TextProfile("original clipboard text");
        await manager.AddLocalProfile(profile, token: Token);
        var key = new HistoryRecordKey(profile.Type, await profile.GetHash(Token));
        var image = new HistoryRecord { Type = ProfileType.Image, Hash = key.Hash, Text = "icon.png", Stared = true };
        fixture.Db.Add(image);
        await fixture.Db.SaveChangesAsync(Token);

        await manager.SetTitleAsync(key, "Checklist", false, Token);
        await manager.SetTitleAsync(HistoryRecordKey.From(image), "Checklist", false, Token);
        await manager.AddLocalProfile(profile, token: Token);

        var contentOnly = new HistoryQueryPara { SearchText = "CHECKLIST" };
        Assert.IsEmpty(await manager.GetHistoryAsync(contentOnly, token: Token));
        var titles = contentOnly with { SearchTitles = true };
        var matches = await manager.GetHistoryAsync(titles, token: Token);
        Assert.HasCount(2, matches);
        Assert.AreEqual(profile.DisplayText, matches.Single(r => r.Type == profile.Type).Text);
        var starred = await manager.GetHistoryAsync(titles with { Starred = true }, minSize: 1, token: Token);
        Assert.AreEqual("icon.png", starred.Single().Text);
        Assert.AreEqual(key, HistoryRecordKey.From((await manager.GetHistoryAsync(
            titles with { SearchText = "original" }, token: Token)).Single()));

        await manager.SetTitleAsync(key, "", false, Token);
        Assert.AreEqual(image.ID, (await manager.GetHistoryAsync(titles, token: Token)).Single().ID);
        await manager.DeleteHistory(image, Token);
        Assert.IsFalse(await manager.SetTitleAsync(HistoryRecordKey.From(image), "deleted", false, Token));
        Assert.IsEmpty(await manager.GetHistoryAsync(titles, token: Token));
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
            "CREATE TRIGGER fail_title BEFORE UPDATE OF Title ON HistoryRecords BEGIN SELECT RAISE(ABORT, 'write failed'); END;", Token);

        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => fixture.Manager.SetTitleAsync(key, "note", true, Token));

        fixture.Db.Add(new HistoryRecord { Type = ProfileType.Text, Hash = "DEF" });
        await fixture.Db.SaveChangesAsync(Token);
        var unchanged = await fixture.Db.HistoryRecords.AsNoTracking().SingleAsync(r => r.Hash == key.Hash, Token);
        Assert.IsNull(unchanged.Title);
        Assert.AreEqual(HistorySyncStatus.Synced, unchanged.SyncStatus);
        await fixture.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_title", Token);
        Assert.IsTrue(await fixture.Manager.SetTitleAsync(key, "note", true, Token));
        var saved = await fixture.Db.HistoryRecords.AsNoTracking().SingleAsync(r => r.Hash == key.Hash, Token);
        Assert.AreEqual("note", saved.Title);
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

        await fixture.Manager.SetTitleAsync(HistoryRecordKey.From(record), "note", true, Token);
        var saved = await fixture.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.AreEqual(int.MaxValue, saved.Version);
        Assert.AreEqual(HistorySyncStatus.NeedSync, saved.SyncStatus);
    }
}
