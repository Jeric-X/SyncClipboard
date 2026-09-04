using System.IO.Compression;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Data.Sqlite;
using SyncClipboard.Server.Core.Controllers;
using SyncClipboard.Server.Core.Exceptions;
using SyncClipboard.Server.Core.Hubs;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Server.Core.Services;
using SyncClipboard.Server.Core.Services.History;
using SyncClipboard.Server.Core.Utilities.History;
using SyncClipboard.Shared;
using SyncClipboard.Shared.Profiles;
using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Test;

[TestClass]
public class HistoryTransferDataHashTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task AddRecordDto_GroupWithMatchingArchiveHashStillRequiresSemanticValidation()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await TestFixture.CreateAsync(token);
        var archivePath = Path.Combine(fixture.RootDirectory, "invalid-group.zip");
        await CreateArchiveAsync(archivePath, "unexpected.txt", "unexpected", token);
        var transferDataHash = await Utility.CalculateFileSHA256(archivePath, token);
        var dto = CreateGroupDto(new string('A', 64), transferDataHash);

        await using var stream = File.OpenRead(archivePath);
        await Assert.ThrowsExactlyAsync<HistoryTransferDataException>(
            () => fixture.Service.AddRecordDto("user", dto, stream, token));

        Assert.AreEqual(0, await fixture.DbContext.HistoryRecords.CountAsync(token));
    }

    [TestMethod]
    public async Task AddRecordDto_ValidGroupPersistsServerCalculatedArchiveHash()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await TestFixture.CreateAsync(token);
        var sourceFile = Path.Combine(fixture.RootDirectory, "source.txt");
        await File.WriteAllTextAsync(sourceFile, "content", token);
        var sourceProfile = new GroupProfile([sourceFile]);
        var archivePath = await sourceProfile.PrepareTransferData(
            Path.Combine(fixture.RootDirectory, "client"),
            token);
        Assert.IsNotNull(archivePath);

        var transferDataHash = await Utility.CalculateFileSHA256(archivePath, token);
        var dto = CreateGroupDto(await sourceProfile.GetHash(token), transferDataHash);
        await using var stream = File.OpenRead(archivePath);

        var result = await fixture.Service.AddRecordDto("user", dto, stream, token);
        var entity = await fixture.DbContext.HistoryRecords.SingleAsync(token);

        Assert.AreEqual(transferDataHash, result.TransferDataHash);
        Assert.AreEqual(transferDataHash, entity.TransferDataHash);
        Assert.IsFalse(string.IsNullOrEmpty(entity.TransferDataFile));
    }

    [TestMethod]
    public async Task AddRecordDto_ResurrectedGroupUsesIncomingArchiveHash()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await TestFixture.CreateAsync(token);
        var sourceFile = Path.Combine(fixture.RootDirectory, "resurrect-source.txt");
        await File.WriteAllTextAsync(sourceFile, "resurrect", token);
        var sourceProfile = new GroupProfile([sourceFile]);
        var archivePath = await sourceProfile.PrepareTransferData(
            Path.Combine(fixture.RootDirectory, "resurrect-client"),
            token);
        Assert.IsNotNull(archivePath);
        var incomingTransferDataHash = await Utility.CalculateFileSHA256(archivePath, token);
        var dto = CreateGroupDto(await sourceProfile.GetHash(token), incomingTransferDataHash);
        var existing = dto.ToEntity("user");
        existing.IsDeleted = true;
        existing.TransferDataFile = "deleted.zip";
        existing.TransferDataHash = new string('A', 64);
        fixture.DbContext.HistoryRecords.Add(existing);
        await fixture.DbContext.SaveChangesAsync(token);

        await using var stream = File.OpenRead(archivePath);
        var result = await fixture.Service.AddRecordDto("user", dto, stream, token);

        Assert.IsFalse(result.IsDeleted);
        Assert.AreEqual(incomingTransferDataHash, result.TransferDataHash);
        Assert.AreEqual(incomingTransferDataHash, existing.TransferDataHash);
    }

    [TestMethod]
    public async Task GetTransferData_KnownArchiveHashSkipsGroupSemanticValidationButRejectsByteChanges()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await TestFixture.CreateAsync(token);
        var profileHash = new string('B', 64);
        var workingDirectory = Profile.CreateWorkingDir(
            fixture.PersistentDirectory,
            ProfileType.Group,
            profileHash);
        var archivePath = Path.Combine(workingDirectory, "verified.zip");
        await CreateArchiveAsync(archivePath, "different.txt", "different", token);
        var transferDataHash = await Utility.CalculateFileSHA256(archivePath, token);
        fixture.DbContext.HistoryRecords.Add(new HistoryRecordEntity
        {
            UserId = "user",
            Type = ProfileType.Group,
            Hash = profileHash,
            TransferDataFile = Path.GetFileName(archivePath),
            TransferDataHash = transferDataHash,
        });
        await fixture.DbContext.SaveChangesAsync(token);

        var result = await fixture.Service.GetTransferDataFileByProfileId(
            "user",
            Profile.GetProfileId(ProfileType.Group, profileHash),
            token);
        Assert.AreEqual(archivePath, result);

        await File.WriteAllTextAsync(archivePath, "changed", token);
        await Assert.ThrowsExactlyAsync<HistoryTransferDataException>(
            () => fixture.Service.GetTransferDataFileByProfileId(
                "user",
                Profile.GetProfileId(ProfileType.Group, profileHash),
                token));
    }

    [TestMethod]
    public async Task GetTransferData_MissingGroupArchiveIsRegeneratedFromExtractedFiles()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await TestFixture.CreateAsync(token);
        var sourceFile = Path.Combine(fixture.RootDirectory, "regenerate-source.txt");
        await File.WriteAllTextAsync(sourceFile, "regenerate", token);
        var sourceProfile = new GroupProfile([sourceFile]);
        var archivePath = await sourceProfile.PrepareTransferData(
            Path.Combine(fixture.RootDirectory, "regenerate-client"),
            token);
        Assert.IsNotNull(archivePath);
        var dto = CreateGroupDto(
            await sourceProfile.GetHash(token),
            await Utility.CalculateFileSHA256(archivePath, token));
        await using (var stream = File.OpenRead(archivePath))
        {
            await fixture.Service.AddRecordDto("user", dto, stream, token);
        }

        var entity = await fixture.DbContext.HistoryRecords.SingleAsync(token);
        var storedArchivePath = Profile.GetFullPath(
            fixture.PersistentDirectory,
            entity.Type,
            entity.Hash,
            entity.TransferDataFile);
        Assert.IsNotNull(storedArchivePath);
        File.Delete(storedArchivePath);

        var regeneratedPath = await fixture.Service.GetTransferDataFileByProfileId(
            "user",
            Profile.GetProfileId(ProfileType.Group, entity.Hash),
            token);

        Assert.IsNotNull(regeneratedPath);
        Assert.IsTrue(File.Exists(regeneratedPath));
        Assert.AreEqual(
            await Utility.CalculateFileSHA256(regeneratedPath, token),
            entity.TransferDataHash);
    }

    [TestMethod]
    public async Task PutSyncProfile_GroupWithMatchingArchiveHashStillRequiresSemanticValidation()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await TestFixture.CreateAsync(token);
        var fileDirectory = Path.Combine(fixture.ServerEnv.GetDataRootPath(), "file");
        Directory.CreateDirectory(fileDirectory);
        var archivePath = Path.Combine(fileDirectory, "ordinary.zip");
        await CreateArchiveAsync(archivePath, "unexpected.txt", "unexpected", token);
        var transferDataHash = await Utility.CalculateFileSHA256(archivePath, token);
        var controller = new SyncClipboardController(
            null!,
            null!,
            fixture.ServerEnv,
            fixture.Service);
        var dto = new ProfileDto
        {
            Type = ProfileType.Group,
            Hash = new string('C', 64),
            Text = "group",
            HasData = true,
            DataName = Path.GetFileName(archivePath),
            TransferDataHash = transferDataHash,
            Size = 1,
        };

        var result = await controller.PutSyncProfile(dto, token);

        Assert.IsInstanceOfType<BadRequestObjectResult>(result);
        Assert.AreEqual(0, await fixture.DbContext.HistoryRecords.CountAsync(token));
    }

    [TestMethod]
    public async Task PutSyncProfile_ValidGroupPersistsAndPublishesServerVerifiedHash()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await TestFixture.CreateAsync(token);
        var sourceFile = Path.Combine(fixture.RootDirectory, "ordinary-source.txt");
        await File.WriteAllTextAsync(sourceFile, "ordinary", token);
        var sourceProfile = new GroupProfile([sourceFile]);
        var clientArchivePath = await sourceProfile.PrepareTransferData(
            Path.Combine(fixture.RootDirectory, "ordinary-client"),
            token);
        Assert.IsNotNull(clientArchivePath);
        var dto = await sourceProfile.ToProfileDto(token);
        var fileDirectory = Path.Combine(fixture.ServerEnv.GetDataRootPath(), "file");
        Directory.CreateDirectory(fileDirectory);
        File.Copy(clientArchivePath, Path.Combine(fileDirectory, dto.DataName!));
        var hubContext = new TestHubContext();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = new SyncClipboardController(
            hubContext,
            cache,
            fixture.ServerEnv,
            fixture.Service);

        var result = await controller.PutSyncProfile(dto, token);

        Assert.IsInstanceOfType<OkResult>(result);
        var entity = await fixture.DbContext.HistoryRecords.SingleAsync(token);
        Assert.AreEqual(dto.TransferDataHash, entity.TransferDataHash);
        Assert.AreEqual(dto.TransferDataHash, hubContext.Client.LastProfile?.TransferDataHash);
    }

    private static HistoryRecordDto CreateGroupDto(string profileHash, string transferDataHash)
    {
        return new HistoryRecordDto
        {
            Hash = profileHash,
            TransferDataHash = transferDataHash,
            Type = ProfileType.Group,
            Text = "group",
            Size = 1,
            HasData = true,
        };
    }

    private static async Task CreateArchiveAsync(
        string archivePath,
        string entryName,
        string content,
        CancellationToken token)
    {
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry(entryName);
        await using var writer = new StreamWriter(entry.Open());
        await writer.WriteAsync(content.AsMemory(), token);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private TestFixture(
            string rootDirectory,
            string persistentDirectory,
            HistoryDbContext dbContext,
            ServerEnvProvider serverEnv,
            HistoryService service)
        {
            RootDirectory = rootDirectory;
            PersistentDirectory = persistentDirectory;
            DbContext = dbContext;
            ServerEnv = serverEnv;
            Service = service;
        }

        public string RootDirectory { get; }
        public string PersistentDirectory { get; }
        public HistoryDbContext DbContext { get; }
        public ServerEnvProvider ServerEnv { get; }
        public HistoryService Service { get; }

        public static async Task<TestFixture> CreateAsync(CancellationToken token)
        {
            var rootDirectory = Path.Combine(
                Path.GetTempPath(),
                $"SyncClipboard-HistoryTransferDataHashTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(rootDirectory);
            var dbPath = Path.Combine(rootDirectory, "history.db");
            var options = new DbContextOptionsBuilder<HistoryDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;
            var serverEnv = new ServerEnvProvider(new TestWebHostEnvironment(rootDirectory));
            var dbContext = new HistoryDbContext(options, serverEnv);
            await dbContext.Database.EnsureCreatedAsync(token);
            var persistentDirectory = serverEnv.GetPersistentDir();
            Directory.CreateDirectory(persistentDirectory);
            var service = new HistoryService(
                dbContext,
                new TestProfileEnv(persistentDirectory),
                null!);
            return new TestFixture(rootDirectory, persistentDirectory, dbContext, serverEnv, service);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Directory.Delete(RootDirectory, recursive: true);
        }
    }

    private sealed class TestProfileEnv(string persistentDirectory) : IProfileEnv
    {
        public string GetPersistentDir() => persistentDirectory;
        public string GetHistoryPersistentDir() => persistentDirectory;
    }

    private sealed class TestWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = nameof(HistoryTransferDataHashTests);
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TestHubContext : IHubContext<SyncClipboardHub, ISyncClipboardClient>
    {
        public TestSyncClipboardClient Client { get; } = new();
        public IHubClients<ISyncClipboardClient> Clients => new TestHubClients(Client);
        public IGroupManager Groups { get; } = new TestGroupManager();
    }

    private sealed class TestHubClients(ISyncClipboardClient client) : IHubClients<ISyncClipboardClient>
    {
        public ISyncClipboardClient All => client;
        public ISyncClipboardClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => client;
        public ISyncClipboardClient Client(string connectionId) => client;
        public ISyncClipboardClient Clients(IReadOnlyList<string> connectionIds) => client;
        public ISyncClipboardClient Group(string groupName) => client;
        public ISyncClipboardClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => client;
        public ISyncClipboardClient Groups(IReadOnlyList<string> groupNames) => client;
        public ISyncClipboardClient User(string userId) => client;
        public ISyncClipboardClient Users(IReadOnlyList<string> userIds) => client;
    }

    private sealed class TestSyncClipboardClient : ISyncClipboardClient
    {
        public ProfileDto? LastProfile { get; private set; }

        public Task RemoteProfileChanged(ProfileDto profile)
        {
            LastProfile = profile;
            return Task.CompletedTask;
        }

        public Task RemoteHistoryChanged(HistoryRecordDto historyRecordDto) => Task.CompletedTask;
    }

    private sealed class TestGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
