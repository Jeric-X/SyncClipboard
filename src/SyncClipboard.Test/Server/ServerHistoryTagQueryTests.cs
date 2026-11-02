using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Moq;
using SyncClipboard.Server.Core.Controllers;
using SyncClipboard.Server.Core.Hubs;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Server.Core.Services.History;
using SyncClipboard.Server.Core.Utilities.History;
using SyncClipboard.Shared.Profiles;

namespace SyncClipboard.Test.Server;

[TestClass]
public class ServerHistoryTagQueryTests
{
    public TestContext TestContext { get; set; } = null!;
    private CancellationToken Token => TestContext.CancellationTokenSource.Token;
    private static readonly DateTime Epoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Query_NormalizesNamesAndMatchesWholeTagsCaseSensitively(bool multiple)
    {
        await using var f = await Fixture.CreateAsync(Token);
        f.Add("match", ["é", "工作"]);
        f.Add("substring", ["工作资料"]);
        f.Add("case", ["WORK"]);
        f.Add("lower", ["work"]);
        f.Add("other-user", ["工作"], userId: "other");
        await f.Db.SaveChangesAsync(Token);
        var query = multiple
            ? new HistoryQueryDto { Tags = [" 工作 ", "e\u0301", "工作", "work"] }
            : new HistoryQueryDto { Tags = [" 工作 "] };

        var records = await f.QueryAsync(query);

        string[] expected = multiple ? ["match", "lower"] : ["match"];
        CollectionAssert.AreEquivalent(expected, records.Select(x => x.Hash).ToArray());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Query_FiltersBeforePagingWithoutDuplicatingMultipleMatches(bool sortByLastAccessed)
    {
        await using var f = await Fixture.CreateAsync(Token);
        var expected = new List<HistoryRecordEntity>();
        for (var i = 0; i < 130; i++)
        {
            var entity = f.Add($"record-{i}", i % 2 == 0 ? ["A", "B"] : ["AB"]);
            entity.CreateTime = Epoch.AddMinutes(i);
            entity.LastAccessed = Epoch.AddMinutes(130 - i);
            if (i % 2 == 0)
                expected.Add(entity);
        }
        await f.Db.SaveChangesAsync(Token);
        var ordered = expected.OrderByDescending(x => sortByLastAccessed ? x.LastAccessed : x.CreateTime)
            .Select(x => x.Hash).ToArray();
        var first = await f.QueryAsync(new() { Tags = ["A", "B"], SortByLastAccessed = sortByLastAccessed });
        var second = await f.QueryAsync(new() { Tags = ["A", "B"], Page = 2, SortByLastAccessed = sortByLastAccessed });
        var third = await f.QueryAsync(new() { Tags = ["A", "B"], Page = 3, SortByLastAccessed = sortByLastAccessed });

        Assert.HasCount(50, first);
        Assert.HasCount(15, second);
        Assert.IsEmpty(third);
        CollectionAssert.AreEqual(ordered, first.Concat(second).Select(x => x.Hash).ToArray());
    }

    [TestMethod]
    public async Task Query_AbsentOrEmptyFiltersPreserveUnfilteredResultsAndCanSelectUntagged()
    {
        await using var f = await Fixture.CreateAsync(Token);
        f.Add("tagged", ["A"]);
        f.Add("empty", []);
        f.Add("other-user", [], userId: "other");
        await f.Db.SaveChangesAsync(Token);

        string[] expected = ["tagged", "empty"];
        foreach (var query in new[] { new HistoryQueryDto(), new HistoryQueryDto { Tags = [] } })
            CollectionAssert.AreEquivalent(expected,
                (await f.QueryAsync(query)).Select(x => x.Hash).ToArray());
        var untagged = await f.QueryAsync(new() { Tags = [], OnlyUntagged = true });
        Assert.AreEqual("empty", untagged.Single().Hash);
        Assert.IsEmpty(await f.QueryAsync(new() { Tags = ["missing"] }));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Query_TagFilterComposesWithExistingFiltersAndRetainsDeletionNotifications(bool sortByLastAccessed)
    {
        await using var f = await Fixture.CreateAsync(Token);
        foreach (var hash in new[] { "match", "deleted", "wrong-tag", "wrong-type", "wrong-text", "unstarred",
            "before-window", "after-window", "old-modification" })
        {
            var record = f.Add(hash, hash == "wrong-tag" ? ["B"] : ["A"]);
            record.Type = hash == "wrong-type" ? ProfileType.Image : ProfileType.Text;
            record.Text = hash == "wrong-text" ? "unrelated" : "needle";
            record.Stared = hash != "unstarred";
            record.IsDeleted = hash == "deleted";
            var time = hash == "before-window" ? Epoch.AddMinutes(-1)
                : hash == "after-window" ? Epoch.AddHours(1) : Epoch;
            record.CreateTime = sortByLastAccessed ? Epoch.AddDays(-1) : time;
            record.LastAccessed = sortByLastAccessed ? time : Epoch.AddDays(-1);
            record.LastModified = hash == "old-modification" ? Epoch.AddDays(-1) : Epoch;
        }
        await f.Db.SaveChangesAsync(Token);

        var result = await f.QueryAsync(new()
        {
            Tags = ["A"],
            Types = ProfileTypeFilter.Text,
            SearchText = "needle",
            Starred = true,
            After = Epoch,
            Before = Epoch.AddHours(1),
            ModifiedAfter = Epoch,
            SortByLastAccessed = sortByLastAccessed
        });

        string[] expected = ["match", "deleted"];
        CollectionAssert.AreEquivalent(expected, result.Select(x => x.Hash).ToArray());
        Assert.IsTrue(result.Single(x => x.Hash == "deleted").IsDeleted);
    }

    [TestMethod]
    public async Task Query_InvalidTagNamesReturnBadRequest()
    {
        await using var f = await Fixture.CreateAsync(Token);
        string[] invalidNames = [" ", "bad\nname", new string('x', 65), "\ud800"];
        foreach (var name in invalidNames)
        {
            var query = new HistoryQueryDto { Tags = [name] };
            Assert.IsInstanceOfType<BadRequestObjectResult>((await f.Controller.QueryHistory(query)).Result);
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Service.GetListAsync(
                HistoryService.HARD_CODED_USER_ID, 1, 50,
                tags: [name], token: Token));
        }
        Assert.IsInstanceOfType<BadRequestObjectResult>(
            (await f.Controller.QueryHistory(new() { Tags = [null!] })).Result);
    }

    [TestMethod]
    public async Task Query_RejectsTagAndUntaggedCombination()
    {
        await using var f = await Fixture.CreateAsync(Token);
        var query = new HistoryQueryDto { OnlyUntagged = true, Tags = ["A"] };
        Assert.IsInstanceOfType<BadRequestObjectResult>((await f.Controller.QueryHistory(query)).Result);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Service.GetListAsync(
            HistoryService.HARD_CODED_USER_ID, 1, 50,
            onlyUntagged: true, tags: query.Tags, token: Token));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public async Task Query_FormBindingRejectsExplicitBlankTags(string value)
    {
        await using var f = await Fixture.CreateAsync(Token);
        var query = await BindFormAsync(new() { ["Tag"] = value });

        Assert.IsInstanceOfType<BadRequestObjectResult>((await f.Controller.QueryHistory(query)).Result);
    }

    [TestMethod]
    public async Task Query_FormBindingPreservesAbsentAndValidTagFilters()
    {
        await using var f = await Fixture.CreateAsync(Token);
        f.Add("match", ["A"]);
        f.Add("other", ["B"]);
        await f.Db.SaveChangesAsync(Token);

        Assert.HasCount(2, await f.QueryAsync(await BindFormAsync([])));
        var single = await f.QueryAsync(await BindFormAsync(new() { ["Tag"] = " A " }));
        Assert.AreEqual("match", single.Single().Hash);
        var query = await BindFormAsync(new() { ["Tag"] = new StringValues(["A", "B"]) });
        string[] expected = ["A", "B"];
        CollectionAssert.AreEqual(expected, query.Tags);
        var multiple = await f.QueryAsync(query);
        Assert.HasCount(2, multiple);
    }

    private static async Task<HistoryQueryDto> BindFormAsync(Dictionary<string, StringValues> form)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();
        using var provider = services.BuildServiceProvider();
        var metadata = provider.GetRequiredService<IModelMetadataProvider>().GetMetadataForType(typeof(HistoryQueryDto));
        var bindingInfo = new BindingInfo { BindingSource = BindingSource.Form };
        var binder = provider.GetRequiredService<IModelBinderFactory>().CreateBinder(new ModelBinderFactoryContext
        {
            Metadata = metadata,
            BindingInfo = bindingInfo,
            CacheToken = typeof(HistoryQueryDto)
        });
        var action = new ActionContext(new DefaultHttpContext { RequestServices = provider },
            new RouteData(), new ActionDescriptor());
        var values = new FormValueProvider(BindingSource.Form, new FormCollection(form), CultureInfo.InvariantCulture);
        var context = DefaultModelBindingContext.CreateBindingContext(action, values, metadata, bindingInfo, "");
        await binder.BindModelAsync(context);
        Assert.IsTrue(context.Result.IsModelSet);
        return (HistoryQueryDto)context.Result.Model!;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public HistoryDbContext Db { get; } = new();
        public HistoryService Service { get; private set; } = null!;
        public HistoryController Controller { get; private set; } = null!;

        public static async Task<Fixture> CreateAsync(CancellationToken token)
        {
            var f = new Fixture();
            f.Db.Database.SetConnectionString("Data Source=:memory:");
            await f.Db.Database.OpenConnectionAsync(token);
            await f.Db.Database.MigrateAsync(token);
            var env = new Mock<IProfileEnv>();
            env.Setup(x => x.GetPersistentDir()).Returns(Path.GetTempPath());
            var hub = new Mock<IHubContext<SyncClipboardHub, ISyncClipboardClient>>();
            f.Service = new HistoryService(f.Db, env.Object, hub.Object);
            f.Controller = new HistoryController(f.Service)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestAborted = token } }
            };
            return f;
        }

        public HistoryRecordEntity Add(string hash, string[] tags, string userId = HistoryService.HARD_CODED_USER_ID)
        {
            var record = new HistoryRecordEntity { Hash = hash, Tags = tags, UserId = userId, Type = ProfileType.Text };
            Db.Add(record);
            return record;
        }

        public async Task<List<HistoryRecordDto>> QueryAsync(HistoryQueryDto query)
        {
            var result = await Controller.QueryHistory(query);
            Assert.IsInstanceOfType<OkObjectResult>(result.Result);
            return (List<HistoryRecordDto>)((OkObjectResult)result.Result).Value!;
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
