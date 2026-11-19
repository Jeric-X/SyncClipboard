using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;
using SyncClipboard.Server.Core.Controllers;
using SyncClipboard.Server.Core.Hubs;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Server.Core.Services.History;
using SyncClipboard.Server.Core.Utilities.History;
using SyncClipboard.Shared.Profiles;
using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Test.Server.History;

[TestClass]
public class ServerHistoryTagWriteTests
{
    public TestContext TestContext { get; set; } = null!;
    private CancellationToken Token => TestContext.CancellationTokenSource.Token;

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PostAndPatch_ValidateAndNormalizeTitles(bool patch)
    {
        await using var f = await Fixture.CreateAsync(Token);
        await f.SeedAsync(Token);
        foreach (var title in new[] { "a\nb", "a\tb", "a\u2028b", new string('中', 257) })
        {
            var result = patch ? await f.PatchAsync(null, Token, title: title) : await f.PostAsync(null, Token, title: title);
            Assert.IsInstanceOfType<BadRequestObjectResult>(result);
            var saved = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
            Assert.IsNull(saved.Title);
            Assert.AreEqual(10, saved.Version);
            Assert.IsEmpty(f.Notifications);
        }
        var accepted = patch ? await f.PatchAsync(null, Token, title: " e\u0301 ") : await f.PostAsync(null, Token, title: " e\u0301 ");
        Assert.IsInstanceOfType<OkObjectResult>(accepted);
        Assert.AreEqual("é", (await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token)).Title);
        Assert.AreEqual("é", f.Notifications.Single().Title);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("[\" 工作 \",\"é\",\"é\",\"Work\",\"work\"]")]
    public async Task Post_NewRecordPersistsNormalizedTags(string? tagsJson)
    {
        await using var f = await Fixture.CreateAsync(Token);
        var result = await f.PostAsync(tagsJson, Token);
        Assert.IsInstanceOfType<OkObjectResult>(result);
        var returned = (HistoryRecordDto)((OkObjectResult)result).Value!;
        string[]? expected = tagsJson is null or "null" ? null : tagsJson == "[]" ? [] : ["Work", "work", "é", "工作"];
        CollectionAssert.AreEqual(expected, returned.Tags);
        await AssertSavedAndNotifiedAsync(f, expected);
    }

    [TestMethod]
    [DataRow(false, null)]
    [DataRow(false, "null")]
    [DataRow(false, "[]")]
    [DataRow(false, "[\" 新标签 \",\"新标签\"]")]
    [DataRow(true, null)]
    [DataRow(true, "null")]
    [DataRow(true, "[]")]
    [DataRow(true, "[\" 新标签 \",\"新标签\"]")]
    public async Task PostAndPatch_PreserveUnspecifiedTagsAndApplyExplicitReplacement(bool patch, string? tagsJson)
    {
        await using var f = await Fixture.CreateAsync(Token);
        await f.SeedAsync(Token);
        var result = patch ? await f.PatchAsync(tagsJson, Token) : await f.PostAsync(tagsJson, Token);
        Assert.IsTrue(result is OkResult or OkObjectResult);
        string[] expected = tagsJson is null or "null" ? ["kept"] : tagsJson == "[]" ? [] : ["新标签"];
        await AssertSavedAndNotifiedAsync(f, expected);
        var saved = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.IsTrue(saved.Stared);
        Assert.AreEqual(11, saved.Version);

        if (tagsJson == "[]")
        {
            await f.PatchAsync(null, Token, version: 12);
            var cleared = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
            Assert.IsNotNull(cleared.Tags);
            Assert.IsEmpty(cleared.Tags);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PostAndPatch_StaleWritesReturnServerTagsWithoutChangingOrNotifying(bool patch)
    {
        await using var f = await Fixture.CreateAsync(Token);
        await f.SeedAsync(Token);
        var result = patch
            ? await f.PatchAsync("[]", Token, version: 1)
            : await f.PostAsync("[]", Token, version: 1);

        string[] expected = ["kept"];
        if (patch)
        {
            Assert.IsInstanceOfType<ConflictObjectResult>(result);
            CollectionAssert.AreEqual(expected, ((HistoryRecordUpdateDto)((ConflictObjectResult)result).Value!).Tags);
        }
        else
        {
            Assert.IsInstanceOfType<OkObjectResult>(result);
            CollectionAssert.AreEqual(expected, ((HistoryRecordDto)((OkObjectResult)result).Value!).Tags);
        }
        var saved = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        CollectionAssert.AreEqual(expected, saved.Tags);
        Assert.AreEqual(10, saved.Version);
        Assert.IsFalse(saved.Stared);
        Assert.IsEmpty(f.Notifications);
    }

    [TestMethod]
    [DataRow(false, 1)]
    [DataRow(false, 10)]
    [DataRow(false, 11)]
    [DataRow(true, 1)]
    [DataRow(true, 10)]
    [DataRow(true, 11)]
    public async Task PostAndPatch_BackfillTagsAndNotifyWithoutPromotingMetadata(bool patch, int version)
    {
        await using var f = await Fixture.CreateAsync(Token);
        var entity = await f.SeedAsync(Token);
        entity.Tags = null;
        await f.Db.SaveChangesAsync(Token);
        var modified = entity.LastModified;

        var result = patch
            ? await f.PatchAsync("""[" 工作 ","工作"]""", Token, version)
            : await f.PostAsync("""[" 工作 ","工作"]""", Token, version);

        string[] expectedTags = ["工作"];
        await AssertSavedAndNotifiedAsync(f, expectedTags);
        var saved = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.AreEqual(modified, saved.LastModified);
        Assert.AreEqual(version >= 10, saved.Stared);
        Assert.AreEqual(version < 10 ? 10 : patch ? version : Math.Max(version, 11), saved.Version);
        HistoryRecordUpdateDto returned;
        if (patch && version < 10)
        {
            Assert.IsInstanceOfType<ConflictObjectResult>(result);
            returned = (HistoryRecordUpdateDto)((ConflictObjectResult)result).Value!;
        }
        else
        {
            Assert.IsInstanceOfType<OkObjectResult>(result);
            var value = ((OkObjectResult)result).Value!;
            returned = patch ? (HistoryRecordUpdateDto)value : ((HistoryRecordDto)value).ToUpdateDto();
        }
        CollectionAssert.AreEqual(expectedTags, returned.Tags);
        Assert.AreEqual(saved.LastModified, returned.LastModified!.Value.UtcDateTime);
        Assert.AreEqual(saved.Version, returned.Version);
        Assert.AreEqual(saved.Stared, returned.Starred);
        Assert.AreEqual(saved.LastModified, f.Notifications.Single().LastModified.UtcDateTime);
        var fullHistory = await f.Service.GetListAsync(HistoryService.HARD_CODED_USER_ID, 1, 50, token: Token);
        Assert.HasCount(1, fullHistory);
        CollectionAssert.AreEqual(expectedTags, fullHistory.Single().Tags);
    }

    [TestMethod]
    [DataRow(false, "[]")]
    [DataRow(false, "[\"kept\"]")]
    [DataRow(true, "[]")]
    [DataRow(true, "[\"kept\"]")]
    public async Task PostAndPatch_StaleTagBackfillRespectsAlreadyInitializedTags(bool patch, string existingTags)
    {
        await using var f = await Fixture.CreateAsync(Token);
        var entity = await f.SeedAsync(Token);
        var expectedTags = JsonSerializer.Deserialize<string[]>(existingTags);
        entity.Tags = expectedTags;
        await f.Db.SaveChangesAsync(Token);
        var modified = entity.LastModified;

        _ = patch ? await f.PatchAsync("[\"new\"]", Token, version: 1)
            : await f.PostAsync("[\"new\"]", Token, version: 1);

        var saved = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        CollectionAssert.AreEqual(expectedTags, saved.Tags);
        Assert.AreEqual(modified, saved.LastModified);
        Assert.AreEqual(10, saved.Version);
        Assert.IsFalse(saved.Stared);
        Assert.IsEmpty(f.Notifications);
    }

    [TestMethod]
    [DataRow("[")]
    [DataRow("{}")]
    [DataRow("[1]")]
    public async Task Post_InvalidTagJsonReturnsBadRequestWithoutCreatingRecord(string tagsJson)
    {
        await using var f = await Fixture.CreateAsync(Token);
        Assert.IsInstanceOfType<BadRequestObjectResult>(await f.PostAsync(tagsJson, Token));
        Assert.AreEqual(0, await f.Db.HistoryRecords.CountAsync(Token));
        Assert.IsEmpty(f.Notifications);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PostAndPatch_InvalidTagsDoNotPartiallyUpdateMetadata(bool patch)
    {
        await using var f = await Fixture.CreateAsync(Token);
        await f.SeedAsync(Token);
        string[][] invalidTags = [[null!], [" "], ["bad\nname"], [new string('x', 65)],
            Enumerable.Range(0, 33).Select(i => $"tag{i}").ToArray()];
        foreach (var tags in invalidTags)
        {
            var json = JsonSerializer.Serialize(tags);
            var result = patch ? await f.PatchAsync(json, Token) : await f.PostAsync(json, Token);
            Assert.IsInstanceOfType<BadRequestObjectResult>(result);
            var saved = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
            Assert.AreEqual("kept", saved.Tags!.Single());
            Assert.AreEqual(10, saved.Version);
            Assert.IsFalse(saved.Stared);
            Assert.IsEmpty(f.Notifications);
        }
    }

    [TestMethod]
    public async Task Service_InvalidTagsAreRejectedBeforeRestoringDeletedRecordData()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var entity = await f.SeedAsync(Token);
        entity.IsDeleted = true;
        await f.Db.SaveChangesAsync(Token);
        var incoming = HistoryRecordDto.FromEntity(entity);
        incoming.IsDeleted = false;
        incoming.Tags = [" "];
        var transfer = new Mock<Stream>(MockBehavior.Strict);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Service.AddRecordDto(
            HistoryService.HARD_CODED_USER_ID, incoming, null, transfer.Object, Token));

        transfer.VerifyNoOtherCalls();
        var saved = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.IsTrue(saved.IsDeleted);
        Assert.AreEqual("kept", saved.Tags!.Single());
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(f.Root));
        Assert.IsEmpty(f.Notifications);
    }

    [TestMethod]
    [DataRow(null, null)]
    [DataRow("", "")]
    [DataRow(" null ", "null")]
    [DataRow(" Cafe\u0301 ", "Café")]
    public async Task Post_NewRecordPreservesTitleStates(string? title, string? expected)
    {
        await using var f = await Fixture.CreateAsync(Token);
        var result = await f.PostAsync(null, Token, title: title);
        Assert.IsInstanceOfType<OkObjectResult>(result);
        Assert.AreEqual(expected, ((HistoryRecordDto)((OkObjectResult)result).Value!).Title);
        Assert.AreEqual(expected, (await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token)).Title);
        Assert.AreEqual(expected, f.Notifications.Single().Title);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PostAndPatch_BackfillOnlyUnknownTitlesAndRespectClears(bool patch)
    {
        await using var f = await Fixture.CreateAsync(Token);
        var original = await f.SeedAsync(Token);
        var originalTime = original.LastModified;

        async Task<HistoryRecordUpdateDto> WriteAsync(string? title, int version, bool stale = false)
        {
            if (patch)
            {
                var result = await f.PatchAsync(null, Token, version, title);
                Assert.AreEqual(stale ? typeof(ConflictObjectResult) : typeof(OkObjectResult), result.GetType());
                return (HistoryRecordUpdateDto)((ObjectResult)result).Value!;
            }
            var post = await f.PostAsync(null, Token, version, title);
            Assert.IsInstanceOfType<OkObjectResult>(post);
            return ((HistoryRecordDto)((OkObjectResult)post).Value!).ToUpdateDto();
        }

        Assert.IsNull((await WriteAsync(" ", 1, stale: true)).Title);
        Assert.IsEmpty(f.Notifications);
        var backfilled = await WriteAsync("  应用图标  ", 1, stale: true);
        Assert.AreEqual("应用图标", backfilled.Title);
        Assert.AreEqual(originalTime, backfilled.LastModified!.Value.UtcDateTime);
        Assert.AreEqual(10, backfilled.Version);
        Assert.IsFalse(backfilled.Starred);
        Assert.AreEqual("kept", backfilled.Tags!.Single());
        Assert.AreEqual("应用图标", (await WriteAsync(null, 11)).Title);
        Assert.AreEqual("新标题", (await WriteAsync(" 新标题 ", 12)).Title);
        Assert.AreEqual("", (await WriteAsync("   ", 13)).Title);
        Assert.AreEqual("", (await WriteAsync(null, 14)).Title);
        Assert.AreEqual("", (await WriteAsync("旧标题", 1, stale: true)).Title);
        string[] expectedTitles = ["应用图标", "应用图标", "新标题", "", ""];
        CollectionAssert.AreEqual(expectedTitles, f.Notifications.Select(dto => dto.Title).ToArray());
        Assert.AreEqual("", (await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token)).Title);
    }

    [TestMethod]
    [DataRow("current", "old", 1, "current")]
    [DataRow("", "old", 1, "")]
    [DataRow(null, " old ", 1, "old")]
    [DataRow(null, " ", 1, null)]
    [DataRow("current", " new ", 11, "new")]
    public async Task Post_RestoresDeletedRecordWhileResolvingTitleByFreshness(
        string? currentTitle, string incomingTitle, int version, string? expectedTitle)
    {
        await using var f = await Fixture.CreateAsync(Token);
        var entity = await f.SeedAsync(Token);
        entity.IsDeleted = true;
        entity.Title = currentTitle;
        await f.Db.SaveChangesAsync(Token);

        var result = await f.PostAsync(null, Token, version, incomingTitle);
        Assert.IsInstanceOfType<OkObjectResult>(result);
        var restored = (HistoryRecordDto)((OkObjectResult)result).Value!;
        Assert.IsFalse(restored.IsDeleted);
        Assert.AreEqual(expectedTitle, restored.Title);
        Assert.AreEqual(expectedTitle, f.Notifications.Single().Title);
        Assert.AreEqual(expectedTitle, (await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token)).Title);
    }

    [TestMethod]
    [DataRow(null, "current", 1, "uploaded", "current")]
    [DataRow("current", null, 1, "current", "uploaded")]
    [DataRow("", "", 1, "", "")]
    [DataRow("current", "current", 11, "uploaded", "uploaded")]
    public async Task Post_RestoreResolvesTagsAndTitleIndependently(
        string? currentTag, string? currentTitle, int version, string expectedTag, string expectedTitle)
    {
        await using var f = await Fixture.CreateAsync(Token);
        var entity = await f.SeedAsync(Token);
        entity.IsDeleted = true;
        entity.Tags = currentTag switch { null => null, "" => [], _ => [currentTag] };
        entity.Title = currentTitle;
        await f.Db.SaveChangesAsync(Token);

        var result = await f.PostAsync("""["uploaded"]""", Token, version, "uploaded");
        Assert.IsInstanceOfType<OkObjectResult>(result);
        var restored = (HistoryRecordDto)((OkObjectResult)result).Value!;
        Assert.IsFalse(restored.IsDeleted);
        string[] expectedTags = expectedTag.Length == 0 ? [] : [expectedTag];
        CollectionAssert.AreEqual(expectedTags, restored.Tags);
        Assert.AreEqual(expectedTitle, restored.Title);
        CollectionAssert.AreEqual(expectedTags, f.Notifications.Single().Tags);
        var saved = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        CollectionAssert.AreEqual(expectedTags, saved.Tags);
        Assert.AreEqual(expectedTitle, saved.Title);
    }

    [TestMethod]
    public async Task Post_RetryOfOlderHighVersionRestorePreservesClearedMetadata()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var entity = await f.SeedAsync(Token);
        entity.IsDeleted = true;
        entity.Tags = [];
        entity.Title = "";
        await f.Db.SaveChangesAsync(Token);
        var uploadedAt = entity.LastModified.AddHours(-1);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var result = await f.PostAsync("""["old"]""", Token, 20, "old", uploadedAt);
            Assert.IsInstanceOfType<OkObjectResult>(result);
            var restored = (HistoryRecordDto)((OkObjectResult)result).Value!;
            Assert.IsFalse(restored.IsDeleted);
            Assert.AreEqual("", restored.Title);
            Assert.IsNotNull(restored.Tags);
            Assert.IsEmpty(restored.Tags);
        }
        Assert.HasCount(1, f.Notifications);
        var renamed = await f.PatchAsync(null, Token, 21, "pending rename");
        Assert.IsInstanceOfType<OkObjectResult>(renamed);
        Assert.AreEqual("pending rename", ((HistoryRecordUpdateDto)((OkObjectResult)renamed).Value!).Title);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Query_ModifiedAfterIncludesOfflineRestoreAtServerTime(bool patch)
    {
        await using var f = await Fixture.CreateAsync(Token);
        var entity = await f.SeedAsync(Token);
        entity.IsDeleted = true;
        entity.Title = "current";
        entity.LastModified = DateTime.UtcNow.AddDays(-1);
        await f.Db.SaveChangesAsync(Token);
        var since = DateTime.UtcNow;
        if (patch)
        {
            var dto = new HistoryRecordUpdateDto { IsDelete = false, Version = 11, LastModified = entity.LastModified };
            await f.Service.Update(entity.UserId, entity.Type, entity.Hash, dto, Token);
            await f.Service.Update(entity.UserId, entity.Type, entity.Hash, dto, Token);
        }
        else
            await f.PostAsync(null, Token, 1, "old", entity.LastModified);
        var changes = await f.Service.GetListAsync(HistoryService.HARD_CODED_USER_ID, 1, 50, modifiedAfter: since, token: Token);
        var restored = changes.Single();
        Assert.IsFalse(restored.IsDeleted);
        Assert.AreEqual("current", restored.Title);
        Assert.IsTrue(restored.LastModified.UtcDateTime >= since);
    }

    [TestMethod]
    public async Task Post_RetryPreservesInitializedMetadata()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var entity = await f.SeedAsync(Token);
        entity.IsDeleted = true;
        entity.Title = "";
        await f.Db.SaveChangesAsync(Token);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var result = await f.PostAsync("[]", Token, 1, "old");
            Assert.IsInstanceOfType<OkObjectResult>(result);
            var restored = (HistoryRecordDto)((OkObjectResult)result).Value!;
            Assert.AreEqual("", restored.Title);
            Assert.AreEqual("kept", restored.Tags!.Single());
        }
        Assert.HasCount(1, f.Notifications);
        var (updated, server) = await f.Service.Update(entity.UserId, entity.Type, entity.Hash,
            new HistoryRecordUpdateDto { Title = "edited" }, Token);
        Assert.IsTrue(updated);
        Assert.AreEqual("edited", server?.Title);
    }

    private async Task AssertSavedAndNotifiedAsync(Fixture f, string[]? expected)
    {
        var saved = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        CollectionAssert.AreEqual(expected, saved.Tags);
        Assert.HasCount(1, f.Notifications);
        CollectionAssert.AreEqual(expected, f.Notifications[0].Tags);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private const string Text = "tagged text";
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"SyncClipboard-TagWrites-{Guid.NewGuid():N}");
        public HistoryDbContext Db { get; } = new();
        public List<HistoryRecordDto> Notifications { get; } = [];
        public HistoryService Service { get; private set; } = null!;
        private HistoryController Controller { get; set; } = null!;
        private string Hash { get; set; } = null!;
        private DateTime Timestamp { get; } = DateTime.UtcNow;

        public static async Task<Fixture> CreateAsync(CancellationToken token)
        {
            var f = new Fixture();
            Directory.CreateDirectory(f.Root);
            f.Db.Database.SetConnectionString("Data Source=:memory:");
            await f.Db.Database.OpenConnectionAsync(token);
            await f.Db.Database.MigrateAsync(token);
            f.Hash = await Utility.CalculateSHA256(Text, token);
            var env = new Mock<IProfileEnv>();
            env.Setup(x => x.GetPersistentDir()).Returns(f.Root);
            var client = new Mock<ISyncClipboardClient>();
            client.Setup(x => x.RemoteHistoryChanged(It.IsAny<HistoryRecordDto>()))
                .Callback<HistoryRecordDto>(f.Notifications.Add).Returns(Task.CompletedTask);
            var hub = new Mock<IHubContext<SyncClipboardHub, ISyncClipboardClient>>();
            hub.Setup(x => x.Clients.All).Returns(client.Object);
            f.Service = new HistoryService(f.Db, env.Object, hub.Object);
            f.Controller = new HistoryController(f.Service)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            return f;
        }

        public async Task<HistoryRecordEntity> SeedAsync(CancellationToken token)
        {
            var entity = new HistoryRecordEntity
            {
                UserId = HistoryService.HARD_CODED_USER_ID,
                Type = ProfileType.Text,
                Hash = Hash,
                Text = Text,
                Tags = ["kept"],
                Version = 10,
                LastModified = Timestamp
            };
            Db.Add(entity);
            await Db.SaveChangesAsync(token);
            return entity;
        }

        public async Task<IActionResult> PostAsync(string? tagsJson, CancellationToken token,
            int version = 11, string? title = null, DateTime? modified = null)
        {
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(Hash), "hash");
            content.Add(new StringContent("Text"), "type");
            content.Add(new StringContent(Text), "text");
            content.Add(new StringContent(version.ToString(System.Globalization.CultureInfo.InvariantCulture)), "version");
            content.Add(new StringContent((modified ?? Timestamp).ToString("O")), "lastModified");
            content.Add(new StringContent("true"), "starred");
            if (title is not null)
                content.Add(new StringContent(title), "title");
            if (tagsJson is not null)
                content.Add(new StringContent(tagsJson), "tags");
            await using var body = new MemoryStream(await content.ReadAsByteArrayAsync(token));
            Controller.Request.ContentType = content.Headers.ContentType!.ToString();
            Controller.Request.Body = body;
            return await Controller.Put(token);
        }

        public Task<IActionResult> PatchAsync(string? tagsJson, CancellationToken token, int version = 11, string? title = null)
        {
            var json = tagsJson is null ? "{}" : $"{{\"tags\":{tagsJson}}}";
            var dto = JsonSerializer.Deserialize<HistoryRecordUpdateDto>(json, JsonSerializerOptions.Web)!;
            dto.Title = title;
            dto.Starred = true;
            dto.Version = version;
            dto.LastModified = Timestamp;
            return Controller.Update(ProfileType.Text, Hash, dto, token);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            Directory.Delete(Root, recursive: true);
        }
    }
}
