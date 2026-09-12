using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Data.Sqlite;
using SyncClipboard.Core.Exceptions;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.RemoteServer.Adapter.OfficialServer;
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
        var dto = CreateGroupDto(new string('A', 64));

        await using var stream = File.OpenRead(archivePath);
        await Assert.ThrowsExactlyAsync<HistoryTransferDataException>(
            () => fixture.Service.AddRecordDto("user", dto, transferDataHash, stream, token));

        Assert.AreEqual(0, await fixture.DbContext.HistoryRecords.CountAsync(token));
    }

    [TestMethod]
    public async Task AddRecordDto_RejectsDeclaredHashThatDoesNotMatchRequestBody()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await TestFixture.CreateAsync(token);
        var archivePath = Path.Combine(fixture.RootDirectory, "mismatched-group.zip");
        await CreateArchiveAsync(archivePath, "unexpected.txt", "unexpected", token);
        var dto = CreateGroupDto(new string('A', 64));

        await using var stream = File.OpenRead(archivePath);
        await Assert.ThrowsExactlyAsync<HistoryTransferDataException>(
            () => fixture.Service.AddRecordDto(
                "user",
                dto,
                new string('B', 64),
                stream,
                token));

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
        var archivePath = (await sourceProfile.PrepareTransferData(
            Path.Combine(fixture.RootDirectory, "client"),
            token))?.Path;
        Assert.IsNotNull(archivePath);

        var transferDataHash = await Utility.CalculateFileSHA256(archivePath, token);
        var dto = CreateGroupDto(await sourceProfile.GetHash(token));
        await using var stream = File.OpenRead(archivePath);

        await fixture.Service.AddRecordDto("user", dto, transferDataHash, stream, token);
        var entity = await fixture.DbContext.HistoryRecords.SingleAsync(token);

        Assert.AreEqual(transferDataHash, entity.TransferDataHash);
        Assert.IsFalse(string.IsNullOrEmpty(entity.TransferDataFile));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task AddRecordDto_ExistingDataOnlyAcceptsVerifiedReplacementWhenDeleted(bool deleted, bool invalidHash)
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await TestFixture.CreateAsync(token);
        var sourceFile = Path.Combine(fixture.RootDirectory, "existing.txt");
        await File.WriteAllTextAsync(sourceFile, "content", token);
        var sourceProfile = new GroupProfile([sourceFile]);
        var archivePath = (await sourceProfile.PrepareTransferData(Path.Combine(fixture.RootDirectory, "client"), token))?.Path;
        Assert.IsNotNull(archivePath);
        var hash = sourceProfile.TransferDataHash!;
        var dto = CreateGroupDto(await sourceProfile.GetHash(token));
        await using (var first = File.OpenRead(archivePath))
            await fixture.Service.AddRecordDto("user", dto, hash, first, token);

        var existing = await fixture.DbContext.HistoryRecords.SingleAsync(token);
        existing.IsDeleted = deleted;
        await fixture.DbContext.SaveChangesAsync(token);
        Assert.IsTrue(await existing.ToProfile(fixture.PersistentDirectory).IsLocalDataValid(false, token));

        await using var stream = File.OpenRead(archivePath);
        if (deleted && invalidHash)
        {
            await Assert.ThrowsExactlyAsync<HistoryTransferDataException>(
                () => fixture.Service.AddRecordDto("user", dto, new string('A', 64), stream, token));
            Assert.IsTrue(existing.IsDeleted);
        }
        else
        {
            // 未删除的重复记录忽略上传内容；已删除记录必须接收并验证新文件。
            var result = await fixture.Service.AddRecordDto("user", dto, deleted ? hash : new string('A', 64), stream, token);
            Assert.IsFalse(result.IsDeleted);
            Assert.AreEqual(hash, existing.TransferDataHash);
            Assert.AreEqual(deleted ? stream.Length : 0, stream.Position);
            Assert.IsTrue(await existing.ToProfile(fixture.PersistentDirectory).IsLocalDataValid(false, token));
        }
        Assert.AreEqual(1, await fixture.DbContext.HistoryRecords.CountAsync(token));
    }

    [TestMethod]
    public async Task AddRecordDto_ResurrectedGroupUsesIncomingArchiveHash()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await TestFixture.CreateAsync(token);
        var sourceFile = Path.Combine(fixture.RootDirectory, "resurrect-source.txt");
        await File.WriteAllTextAsync(sourceFile, "resurrect", token);
        var sourceProfile = new GroupProfile([sourceFile]);
        var archivePath = (await sourceProfile.PrepareTransferData(
            Path.Combine(fixture.RootDirectory, "resurrect-client"),
            token))?.Path;
        Assert.IsNotNull(archivePath);
        var incomingTransferDataHash = await Utility.CalculateFileSHA256(archivePath, token);
        var dto = CreateGroupDto(await sourceProfile.GetHash(token));
        var existing = dto.ToEntity("user");
        existing.IsDeleted = true;
        existing.TransferDataFile = "deleted.zip";
        existing.TransferDataHash = new string('A', 64);
        fixture.DbContext.HistoryRecords.Add(existing);
        await fixture.DbContext.SaveChangesAsync(token);

        await using var stream = File.OpenRead(archivePath);
        var result = await fixture.Service.AddRecordDto(
            "user",
            dto,
            incomingTransferDataHash,
            stream,
            token);

        Assert.IsFalse(result.IsDeleted);
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

        var result = await fixture.Service.GetTransferDataByProfileId(
            "user",
            Profile.GetProfileId(ProfileType.Group, profileHash),
            token);
        Assert.AreEqual(archivePath, result?.Path);
        Assert.AreEqual(transferDataHash, result?.Hash);

        await File.WriteAllTextAsync(archivePath, "changed", token);
        await Assert.ThrowsExactlyAsync<HistoryTransferDataException>(
            () => fixture.Service.GetTransferDataByProfileId(
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
        var archivePath = (await sourceProfile.PrepareTransferData(
            Path.Combine(fixture.RootDirectory, "regenerate-client"),
            token))?.Path;
        Assert.IsNotNull(archivePath);
        var transferDataHash = await Utility.CalculateFileSHA256(archivePath, token);
        var dto = CreateGroupDto(await sourceProfile.GetHash(token));
        await using (var stream = File.OpenRead(archivePath))
        {
            await fixture.Service.AddRecordDto(
                "user",
                dto,
                transferDataHash,
                stream,
                token);
        }

        var entity = await fixture.DbContext.HistoryRecords.SingleAsync(token);
        var storedArchivePath = Profile.GetFullPath(
            fixture.PersistentDirectory,
            entity.Type,
            entity.Hash,
            entity.TransferDataFile);
        Assert.IsNotNull(storedArchivePath);
        File.Delete(storedArchivePath);

        var regenerated = await fixture.Service.GetTransferDataByProfileId(
            "user",
            Profile.GetProfileId(ProfileType.Group, entity.Hash),
            token);

        Assert.IsNotNull(regenerated);
        Assert.IsTrue(File.Exists(regenerated.Path));
        Assert.AreEqual(
            await Utility.CalculateFileSHA256(regenerated.Path, token),
            regenerated.Hash);
        Assert.AreEqual(
            regenerated.Hash,
            entity.TransferDataHash);
    }

    [TestMethod]
    public async Task GetTransferData_CorruptedGroupArchiveIsRegeneratedFromExtractedFiles()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await TestFixture.CreateAsync(token);
        var sourceFile = Path.Combine(fixture.RootDirectory, "corrupt-source.txt");
        await File.WriteAllTextAsync(sourceFile, "regenerate", token);
        var sourceProfile = new GroupProfile([sourceFile]);
        var archivePath = (await sourceProfile.PrepareTransferData(
            Path.Combine(fixture.RootDirectory, "corrupt-client"),
            token))?.Path;
        Assert.IsNotNull(archivePath);
        var transferDataHash = await Utility.CalculateFileSHA256(archivePath, token);
        var dto = CreateGroupDto(await sourceProfile.GetHash(token));
        await using (var stream = File.OpenRead(archivePath))
        {
            await fixture.Service.AddRecordDto(
                "user",
                dto,
                transferDataHash,
                stream,
                token);
        }

        var entity = await fixture.DbContext.HistoryRecords.SingleAsync(token);
        var storedArchivePath = Profile.GetFullPath(
            fixture.PersistentDirectory,
            entity.Type,
            entity.Hash,
            entity.TransferDataFile);
        Assert.IsNotNull(storedArchivePath);
        await File.WriteAllTextAsync(storedArchivePath, "corrupted", token);

        var regenerated = await fixture.Service.GetTransferDataByProfileId(
            "user",
            Profile.GetProfileId(ProfileType.Group, entity.Hash),
            token);

        Assert.IsNotNull(regenerated);
        Assert.IsTrue(File.Exists(regenerated.Path));
        Assert.AreEqual(
            await Utility.CalculateFileSHA256(regenerated.Path, token),
            regenerated.Hash);
        Assert.AreEqual(regenerated.Hash, entity.TransferDataHash);
        Assert.AreEqual(transferDataHash, regenerated.Hash);
    }

    [TestMethod]
    public async Task GetTransferData_UnreadableGroupArchiveIsRegeneratedFromExtractedFiles()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await TestFixture.CreateAsync(token);
        var sourceFile = Path.Combine(fixture.RootDirectory, "locked-source.txt");
        await File.WriteAllTextAsync(sourceFile, "regenerate", token);
        var sourceProfile = new GroupProfile([sourceFile]);
        var archivePath = (await sourceProfile.PrepareTransferData(
            Path.Combine(fixture.RootDirectory, "locked-client"),
            token))?.Path;
        Assert.IsNotNull(archivePath);
        var transferDataHash = await Utility.CalculateFileSHA256(archivePath, token);
        var dto = CreateGroupDto(await sourceProfile.GetHash(token));
        await using (var stream = File.OpenRead(archivePath))
        {
            await fixture.Service.AddRecordDto(
                "user",
                dto,
                transferDataHash,
                stream,
                token);
        }

        var entity = await fixture.DbContext.HistoryRecords.SingleAsync(token);
        var storedArchivePath = Profile.GetFullPath(
            fixture.PersistentDirectory,
            entity.Type,
            entity.Hash,
            entity.TransferDataFile);
        Assert.IsNotNull(storedArchivePath);

        await using var lockedArchive = new FileStream(
            storedArchivePath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);
        var regenerated = await fixture.Service.GetTransferDataByProfileId(
            "user",
            Profile.GetProfileId(ProfileType.Group, entity.Hash),
            token);

        Assert.IsNotNull(regenerated);
        Assert.AreNotEqual(storedArchivePath, regenerated.Path);
        Assert.IsTrue(File.Exists(regenerated.Path));
        Assert.AreEqual(
            await Utility.CalculateFileSHA256(regenerated.Path, token),
            regenerated.Hash);
        Assert.AreEqual(regenerated.Hash, entity.TransferDataHash);
    }

    [TestMethod]
    public async Task GetTransferData_ReturnsHashInResponseHeader()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await TestFixture.CreateAsync(token);
        var sourcePath = Path.Combine(fixture.RootDirectory, "file.bin");
        await File.WriteAllTextAsync(sourcePath, "content", token);
        var profileHash = await new FileProfile(sourcePath).GetHash(token);
        var workingDirectory = Profile.CreateWorkingDir(
            fixture.PersistentDirectory,
            ProfileType.File,
            profileHash);
        var filePath = Path.Combine(workingDirectory, "file.bin");
        await File.WriteAllTextAsync(filePath, "content", token);
        var transferDataHash = await Utility.CalculateFileSHA256(filePath, token);
        fixture.DbContext.HistoryRecords.Add(new HistoryRecordEntity
        {
            UserId = HistoryService.HARD_CODED_USER_ID,
            Type = ProfileType.File,
            Hash = profileHash,
            TransferDataFile = Path.GetFileName(filePath),
            TransferDataHash = transferDataHash,
        });
        await fixture.DbContext.SaveChangesAsync(token);
        var controller = new HistoryController(fixture.Service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

        var result = await controller.GetTransferData(
            Profile.GetProfileId(ProfileType.File, profileHash),
            token);

        Assert.AreEqual(
            transferDataHash,
            controller.Response.Headers[HistoryTransferDataHeaders.TransferDataHash].ToString());
        Assert.IsInstanceOfType<FileStreamResult>(result);
        await ((FileStreamResult)result).FileStream.DisposeAsync();
    }

    [TestMethod]
    public void RemoteHistoryMetadata_DoesNotCreateLocalTransferDataHashBinding()
    {
        var record = CreateGroupDto(new string('E', 64)).ToHistoryRecord();

        Assert.IsNull(record.TransferDataFile);
        Assert.IsNull(record.TransferDataHash);
        Assert.IsFalse(record.IsLocalFileReady);
    }

    [TestMethod]
    public async Task SaveHistoryDataResponse_MatchingHeaderPublishesFileAndReturnsHash()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await TestFixture.CreateAsync(token);
        var bytes = Encoding.UTF8.GetBytes("downloaded history data");
        var transferDataHash = await Utility.CalculateSHA256(bytes, token);
        using var response = CreateTransferDataResponse(bytes, transferDataHash);
        var localPath = Path.Combine(fixture.RootDirectory, "download", "data.bin");

        var actualHash = await OfficialAdapter.SaveHistoryDataResponseAsync(
            response,
            localPath,
            progress: null,
            token);

        Assert.AreEqual(transferDataHash, actualHash);
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(localPath, token));
    }

    [TestMethod]
    public async Task SaveHistoryDataResponse_MismatchedHeaderDoesNotReplaceExistingFile()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await TestFixture.CreateAsync(token);
        var localPath = Path.Combine(fixture.RootDirectory, "data.bin");
        await File.WriteAllTextAsync(localPath, "existing", token);
        using var response = CreateTransferDataResponse(
            Encoding.UTF8.GetBytes("rejected"),
            new string('A', 64));

        await Assert.ThrowsExactlyAsync<RemoteHistoryDataRejectedException>(
            () => OfficialAdapter.SaveHistoryDataResponseAsync(
                response,
                localPath,
                progress: null,
                token));

        Assert.AreEqual("existing", await File.ReadAllTextAsync(localPath, token));
        Assert.AreEqual(0, Directory.GetFiles(fixture.RootDirectory, "*.download").Length);
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
        var clientArchivePath = (await sourceProfile.PrepareTransferData(
            Path.Combine(fixture.RootDirectory, "ordinary-client"),
            token))?.Path;
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

    private static HistoryRecordDto CreateGroupDto(string profileHash)
    {
        return new HistoryRecordDto
        {
            Hash = profileHash,
            Type = ProfileType.Group,
            Text = "group",
            Size = 1,
            HasData = true,
        };
    }

    private static HttpResponseMessage CreateTransferDataResponse(
        byte[] bytes,
        string transferDataHash)
    {
        var response = new HttpResponseMessage
        {
            Content = new ByteArrayContent(bytes),
        };
        response.Headers.Add(
            HistoryTransferDataHeaders.TransferDataHash,
            transferDataHash);
        return response;
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
