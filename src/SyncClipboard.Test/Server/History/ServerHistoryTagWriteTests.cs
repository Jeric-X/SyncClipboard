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
    public async Task PostAndPatch_BackfillMissingTagsAndExposeChangeToIncrementalSync(bool patch, int version)
    {
        await using var f = await Fixture.CreateAsync(Token);
        var entity = await f.SeedAsync(Token);
        entity.Tags = null;
        await f.Db.SaveChangesAsync(Token);
        var before = DateTime.UtcNow;

        var result = patch
            ? await f.PatchAsync("""[" 工作 ","工作"]""", Token, version)
            : await f.PostAsync("""[" 工作 ","工作"]""", Token, version);

        string[] expectedTags = ["工作"];
        await AssertSavedAndNotifiedAsync(f, expectedTags);
        var saved = await f.Db.HistoryRecords.AsNoTracking().SingleAsync(Token);
        Assert.IsTrue(saved.LastModified >= before);
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
        var incremental = await f.Service.GetListAsync(HistoryService.HARD_CODED_USER_ID,
            1, 50, modifiedAfter: before, token: Token);
        Assert.HasCount(1, incremental);
        CollectionAssert.AreEqual(expectedTags, incremental.Single().Tags);
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

        public async Task<IActionResult> PostAsync(string? tagsJson, CancellationToken token, int version = 11)
        {
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(Hash), "hash");
            content.Add(new StringContent("Text"), "type");
            content.Add(new StringContent(Text), "text");
            content.Add(new StringContent(version.ToString(System.Globalization.CultureInfo.InvariantCulture)), "version");
            content.Add(new StringContent(Timestamp.ToString("O")), "lastModified");
            content.Add(new StringContent("true"), "starred");
            if (tagsJson is not null)
                content.Add(new StringContent(tagsJson), "tags");
            await using var body = new MemoryStream(await content.ReadAsByteArrayAsync(token));
            Controller.Request.ContentType = content.Headers.ContentType!.ToString();
            Controller.Request.Body = body;
            return await Controller.Put(token);
        }

        public Task<IActionResult> PatchAsync(string? tagsJson, CancellationToken token, int version = 11)
        {
            var json = tagsJson is null ? "{}" : $"{{\"tags\":{tagsJson}}}";
            var dto = JsonSerializer.Deserialize<HistoryRecordUpdateDto>(json, JsonSerializerOptions.Web)!;
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
