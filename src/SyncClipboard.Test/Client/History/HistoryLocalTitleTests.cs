using Microsoft.EntityFrameworkCore;
using SyncClipboard.Core.Models;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Shared.Profiles;
using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Test.Client.History;

[TestClass]
public class HistoryLocalTitleTests
{
    public TestContext TestContext { get; set; } = null!;
    private CancellationToken Token => TestContext.CancellationTokenSource.Token;

    [TestMethod]
    public void TitleMapping_NormalizesReceivedValuesBeforeChangingTrackedMetadata()
    {
        var dto = new HistoryRecordDto { Title = " e\u0301 " };
        Assert.AreEqual("é", dto.ToEntity("user").Title);
        var record = MapperExtensions.ToHistoryRecord(dto);
        Assert.AreEqual("é", record.Title);
        dto.Title = "a\nb";
        dto.Tags = ["new"];
        record.Tags = ["kept"];
        Assert.ThrowsExactly<HistoryTitleValidationException>(() => MapperExtensions.ApplyChangesFromRemote(record, dto));
        Assert.AreEqual("kept", record.Tags!.Single());
        Assert.AreEqual("é", record.Title);
        var update = new HistoryRecordUpdateDto { Title = " e\u0301 " };
        MapperExtensions.ApplyFromServerUpdateDto(record, update);
        Assert.AreEqual("é", record.Title);
    }

    [TestMethod]
    public async Task UnicodeTitle_SaveSearchAndInvalidEditPreserveContentAndMetadata()
    {
        await using var fixture = await HistoryRetentionTests.Fixture.CreateAsync(Token);
        var titleRecord = new HistoryRecord { Type = ProfileType.Text, Hash = "ABC", Text = "original" };
        var textRecord = new HistoryRecord { Type = ProfileType.Text, Hash = "DEF", Text = "Cafe\u0301" };
        fixture.Db.AddRange(titleRecord, textRecord);
        await fixture.Db.SaveChangesAsync(Token);
        var key = HistoryRecordKey.From(titleRecord);
        Assert.IsTrue(await fixture.Manager.SetTitleAsync(key, " Cafe\u0301 ", Token));
        var modified = titleRecord.LastModified;
        var version = titleRecord.Version;
        Assert.IsFalse(await fixture.Manager.SetTitleAsync(key, "Café", Token));
        await Assert.ThrowsExactlyAsync<HistoryTitleValidationException>(() => fixture.Manager.SetTitleAsync(key, "a\nb", Token));
        await Assert.ThrowsExactlyAsync<HistoryTitleValidationException>(() => fixture.Manager.SetTitleAsync(key, new string('a', 257), Token));
        Assert.AreEqual(modified, titleRecord.LastModified);
        Assert.AreEqual(version, titleRecord.Version);
        Assert.AreEqual("original", titleRecord.Text);
        Assert.AreEqual("Café", titleRecord.Title);
        var query = new HistoryQueryPara { SearchText = "Cafe\u0301", SearchTitles = true };
        Assert.HasCount(2, await fixture.Manager.GetHistoryAsync(query, token: Token));
        Assert.AreEqual(textRecord.ID, (await fixture.Manager.GetHistoryAsync(query with { SearchTitles = false }, token: Token)).Single().ID);
        Assert.AreEqual(titleRecord.ID, (await fixture.Manager.GetHistoryAsync(query with { SearchText = "Café" }, token: Token)).Single().ID);
        Assert.IsEmpty(await fixture.Manager.GetHistoryAsync(query with { SearchText = " Café " }, token: Token));
        var stored = await fixture.Db.HistoryRecords.AsNoTracking().SingleAsync(record => record.ID == titleRecord.ID, Token);
        Assert.AreEqual("Café", stored.Title);
        Assert.AreEqual(version, stored.Version);
    }

    [TestMethod]
    [DataRow(false, HistorySyncStatus.Synced, HistorySyncStatus.NeedSync)]
    [DataRow(false, HistorySyncStatus.NeedSync, HistorySyncStatus.NeedSync)]
    [DataRow(false, HistorySyncStatus.LocalOnly, HistorySyncStatus.LocalOnly)]
    [DataRow(true, HistorySyncStatus.Synced, HistorySyncStatus.NeedSync)]
    [DataRow(true, HistorySyncStatus.NeedSync, HistorySyncStatus.NeedSync)]
    [DataRow(true, HistorySyncStatus.LocalOnly, HistorySyncStatus.LocalOnly)]
    public async Task EditAndClear_QueueNonLocalRecordsEvenWhenSyncIsDisabled(
        bool syncEnabled, HistorySyncStatus initialStatus, HistorySyncStatus expectedStatus)
    {
        await using var fixture = await HistoryRetentionTests.Fixture.CreateAsync(Token);
        fixture.RuntimeConfig.EnableSyncHistory = syncEnabled;
        var modified = DateTime.UtcNow.AddHours(-1);
        var accessed = modified.AddDays(-1);
        var record = new HistoryRecord
        {
            Type = ProfileType.Text,
            Hash = "ABC",
            Text = "clipboard content",
            SyncStatus = initialStatus,
            LastModified = modified,
            Timestamp = accessed,
            LastAccessed = accessed,
            Version = 7
        };
        fixture.Db.Add(record);
        await fixture.Db.SaveChangesAsync(Token);
        var key = new HistoryRecordKey(record.Type, "abc");
        var notifications = 0;
        fixture.Manager.HistoryUpdated += _ => notifications++;

        Assert.IsFalse(await fixture.Manager.SetTitleAsync(key, "", Token));
        Assert.IsNull((await fixture.Db.HistoryRecords.AsNoTracking().SingleAsync(Token)).Title);
        Assert.IsTrue(await fixture.Manager.SetTitleAsync(key, "  Release notes  ", Token));
        var titled = await fixture.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.AreEqual("Release notes", titled.Title);
        Assert.AreEqual(expectedStatus, titled.SyncStatus);
        Assert.AreEqual(8, titled.Version);
        Assert.IsGreaterThan(modified, titled.LastModified);

        Assert.IsFalse(await fixture.Manager.SetTitleAsync(key, "Release notes ", Token));
        var unchanged = await fixture.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.AreEqual(titled.LastModified, unchanged.LastModified);
        Assert.AreEqual(titled.Version, unchanged.Version);
        Assert.IsTrue(await fixture.Manager.SetTitleAsync(key, "  ", Token));
        var cleared = await fixture.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.AreEqual(string.Empty, cleared.Title);
        Assert.AreEqual(9, cleared.Version);
        Assert.AreEqual(expectedStatus, cleared.SyncStatus);
        Assert.AreEqual(accessed, cleared.Timestamp);
        Assert.AreEqual(accessed, cleared.LastAccessed);
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

        await manager.SetTitleAsync(key, "Checklist", Token);
        await manager.SetTitleAsync(HistoryRecordKey.From(image), "Checklist", Token);
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

        await manager.SetTitleAsync(key, "", Token);
        Assert.AreEqual(image.ID, (await manager.GetHistoryAsync(titles, token: Token)).Single().ID);
        await manager.DeleteHistory(image, Token);
        Assert.IsFalse(await manager.SetTitleAsync(HistoryRecordKey.From(image), "deleted", Token));
        Assert.IsEmpty(await manager.GetHistoryAsync(titles, token: Token));
    }

    [TestMethod]
    public async Task FailedRename_PropagatesDatabaseErrorWithoutNotifying()
    {
        await using var fixture = await HistoryRetentionTests.Fixture.CreateAsync(Token);
        fixture.RuntimeConfig.EnableSyncHistory = true;
        var record = new HistoryRecord { Type = ProfileType.Text, Hash = "ABC", SyncStatus = HistorySyncStatus.Synced };
        fixture.Db.Add(record);
        await fixture.Db.SaveChangesAsync(Token);
        var key = HistoryRecordKey.From(record);
        await fixture.Db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER fail_title BEFORE UPDATE OF Title ON HistoryRecords BEGIN SELECT RAISE(ABORT, 'write failed'); END;", Token);

        var notifications = 0;
        fixture.Manager.HistoryUpdated += _ => notifications++;

        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => fixture.Manager.SetTitleAsync(key, "note", Token));

        var unchanged = await fixture.Db.HistoryRecords.AsNoTracking().SingleAsync(r => r.Hash == key.Hash, Token);
        Assert.IsNull(unchanged.Title);
        Assert.AreEqual(HistorySyncStatus.Synced, unchanged.SyncStatus);
        Assert.AreEqual(0, notifications);
    }
}
