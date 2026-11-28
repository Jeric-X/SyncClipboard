using System.IO.Compression;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
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

namespace SyncClipboard.Test.Server.History;

[TestClass]
public class HistoryTransferDataHashTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AddRecordDto_InlineTextWithoutFileRequiresMatchingHash(bool matchingHash)
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await TestFixture.CreateAsync(token);
        const string text = "inline history text";
        var actualHash = await Utility.CalculateSHA256(text, token);
        var dto = new HistoryRecordDto
        {
            Type = ProfileType.Text,
            Text = text,
            Hash = matchingHash ? actualHash.ToLowerInvariant() : new string('A', 64),
            Size = text.Length,
            HasData = false,
        };

        if (!matchingHash)
        {
            await Assert.ThrowsExactlyAsync<ArgumentException>(
                () => fixture.Service.AddRecordDto("user", dto, null, null, token));
            Assert.AreEqual(0, await fixture.DbContext.HistoryRecords.CountAsync(token));
            return;
        }

        var result = await fixture.Service.AddRecordDto("user", dto, null, null, token);
        var entity = await fixture.DbContext.HistoryRecords.SingleAsync(token);

        Assert.AreEqual(text, entity.Text);
        Assert.IsTrue(Utility.SHA256Same(actualHash, entity.Hash));
        Assert.IsTrue(Utility.SHA256Same(actualHash, result.Hash));
        Assert.IsNull(entity.TransferDataHash);
    }

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
            () => fixture.Service.AddRecordDto("user", dto, new string('B', 64), stream, token));

        Assert.AreEqual(0, await fixture.DbContext.HistoryRecords.CountAsync(token));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AddRecordDto_RejectedUploadPreservesExistingExtractionDirectory(bool mismatchedTransferHash)
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await TestFixture.CreateAsync(token);
        var dto = CreateGroupDto(new string('A', 64));
        var existing = dto.ToEntity("user");
        existing.IsDeleted = true;
        existing.TransferDataFile = "data.zip";
        fixture.DbContext.HistoryRecords.Add(existing);
        await fixture.DbContext.SaveChangesAsync(token);

        var workingDir = Profile.CreateWorkingDir(fixture.PersistentDirectory, dto.Type, dto.Hash);
        var uploadPath = Path.Combine(workingDir, existing.TransferDataFile);
        var oldExtractDir = uploadPath[..^4];
        Directory.CreateDirectory(oldExtractDir);
        var sentinelPath = Path.Combine(oldExtractDir, "keep.txt");
        await File.WriteAllTextAsync(sentinelPath, "existing content", token);
        var archivePath = Path.Combine(fixture.RootDirectory, "incoming.zip");
        await CreateArchiveAsync(archivePath, "unexpected.txt", "unexpected", token);
        var declaredHash = mismatchedTransferHash
            ? new string('B', 64)
            : await Utility.CalculateFileSHA256(archivePath, token);

        await using var stream = File.OpenRead(archivePath);
        await Assert.ThrowsExactlyAsync<HistoryTransferDataException>(
            () => fixture.Service.AddRecordDto("user", dto, declaredHash, stream, token));

        Assert.AreEqual("existing content", await File.ReadAllTextAsync(sentinelPath, token));
        Assert.IsFalse(File.Exists(uploadPath));
        CollectionAssert.AreEqual(new[] { oldExtractDir }, Directory.GetDirectories(workingDir));
        Assert.IsTrue(existing.IsDeleted);
        Assert.AreEqual(1, await fixture.DbContext.HistoryRecords.CountAsync(token));
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
            Path.Combine(fixture.RootDirectory, "client"), token))?.Path;
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
            Path.Combine(fixture.RootDirectory, "resurrect-client"), token))?.Path;
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
        var result = await fixture.Service.AddRecordDto("user", dto, incomingTransferDataHash, stream, token);

        Assert.IsFalse(result.IsDeleted);
        Assert.AreEqual(incomingTransferDataHash, existing.TransferDataHash);
    }

    [TestMethod]
    public async Task GetTransferData_KnownArchiveHashSkipsGroupSemanticValidationButRejectsByteChanges()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await TestFixture.CreateAsync(token);
        var profileHash = new string('B', 64);
        var workingDirectory = Profile.CreateWorkingDir(fixture.PersistentDirectory, ProfileType.Group, profileHash);
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
            "user", Profile.GetProfileId(ProfileType.Group, profileHash), token);
        Assert.AreEqual(archivePath, result?.Path);
        Assert.AreEqual(transferDataHash, result?.Hash);

        await File.WriteAllTextAsync(archivePath, "changed", token);
        await Assert.ThrowsExactlyAsync<HistoryTransferDataException>(
            () => fixture.Service.GetTransferDataByProfileId(
                "user", Profile.GetProfileId(ProfileType.Group, profileHash), token));
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("corrupted")]
    [DataRow("unreadable")]
    public async Task GetTransferData_UnavailableGroupArchiveIsRegeneratedFromExtractedFiles(string archiveState)
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await TestFixture.CreateAsync(token);
        var sourceFile = Path.Combine(fixture.RootDirectory, "regenerate-source.txt");
        await File.WriteAllTextAsync(sourceFile, "regenerate", token);
        var sourceProfile = new GroupProfile([sourceFile]);
        var archivePath = (await sourceProfile.PrepareTransferData(
            Path.Combine(fixture.RootDirectory, "regenerate-client"), token))?.Path;
        Assert.IsNotNull(archivePath);
        var transferDataHash = await Utility.CalculateFileSHA256(archivePath, token);
        var dto = CreateGroupDto(await sourceProfile.GetHash(token));
        await using (var stream = File.OpenRead(archivePath))
        {
            await fixture.Service.AddRecordDto("user", dto, transferDataHash, stream, token);
        }

        var entity = await fixture.DbContext.HistoryRecords.SingleAsync(token);
        var storedArchivePath = Profile.GetFullPath(
            fixture.PersistentDirectory, entity.Type, entity.Hash, entity.TransferDataFile);
        Assert.IsNotNull(storedArchivePath);
        if (archiveState == "missing")
            File.Delete(storedArchivePath);
        else if (archiveState == "corrupted")
            await File.WriteAllTextAsync(storedArchivePath, "corrupted", token);
        await using var lockedArchive = archiveState == "unreadable"
            ? new FileStream(storedArchivePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
            : null;

        var regenerated = await fixture.Service.GetTransferDataByProfileId(
            "user", Profile.GetProfileId(ProfileType.Group, entity.Hash), token);

        Assert.IsNotNull(regenerated);
        if (archiveState == "unreadable")
            Assert.AreNotEqual(storedArchivePath, regenerated.Path);
        Assert.IsTrue(File.Exists(regenerated.Path));
        Assert.AreEqual(await Utility.CalculateFileSHA256(regenerated.Path, token), regenerated.Hash);
        Assert.AreEqual(regenerated.Hash, entity.TransferDataHash);
        // ZIP entry timestamps can change on regeneration even when the file content is identical.
        using var regeneratedArchive = ZipFile.OpenRead(regenerated.Path);
        Assert.HasCount(1, regeneratedArchive.Entries);
        var entry = regeneratedArchive.Entries.Single();
        Assert.AreEqual(Path.GetFileName(sourceFile), entry.FullName);
        using var reader = new StreamReader(entry.Open());
        Assert.AreEqual("regenerate", await reader.ReadToEndAsync(token));
    }

    [TestMethod]
    public async Task GetTransferData_ReturnsHashInResponseHeader()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await TestFixture.CreateAsync(token);
        var sourcePath = Path.Combine(fixture.RootDirectory, "file.bin");
        await File.WriteAllTextAsync(sourcePath, "content", token);
        var profileHash = await new FileProfile(sourcePath).GetHash(token);
        var workingDirectory = Profile.CreateWorkingDir(fixture.PersistentDirectory, ProfileType.File, profileHash);
        var filePath = Path.Combine(workingDirectory, "file.bin");
        await File.WriteAllTextAsync(filePath, "content", token);
        var transferDataHash = await Utility.CalculateFileSHA256(filePath, token);
        fixture.DbContext.HistoryRecords.Add(new HistoryRecordEntity
        {
            UserId = "default_user",
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
                HttpContext = TestUserContext.Create(token: token),
            },
        };

        var result = await controller.GetTransferData(Profile.GetProfileId(ProfileType.File, profileHash), token);

        Assert.AreEqual(
            transferDataHash, controller.Response.Headers[HistoryTransferDataHeaders.TransferDataHash].ToString());
        Assert.IsInstanceOfType<FileStreamResult>(result);
        await ((FileStreamResult)result).FileStream.DisposeAsync();
    }

    [TestMethod]
    public async Task PutSyncProfile_InvalidInlineTextDoesNotPersistOrPublish()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await TestFixture.CreateAsync(token);
        var hubContext = new TestHubContext();
        var controller = fixture.Clipboard(hubContext);
        var profilePath = Path.Combine(fixture.ServerEnv.GetClipboardDir("default_user"), "SyncClipboard.json");
        var dto = new ProfileDto
        {
            Type = ProfileType.Text,
            Hash = await Utility.CalculateSHA256("original", token),
            Text = "altered",
            HasData = false,
        };

        var result = await controller.PutSyncProfile(dto, token);

        Assert.IsInstanceOfType<BadRequestObjectResult>(result);
        Assert.AreEqual(0, await fixture.DbContext.HistoryRecords.CountAsync(token));
        Assert.IsNull(hubContext.Client.LastProfile);
        Assert.IsFalse(File.Exists(profilePath));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PutSyncProfile_ValidInlineTextPersistsAndPublishes(bool omitHash)
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await TestFixture.CreateAsync(token);
        var hubContext = new TestHubContext();
        var controller = fixture.Clipboard(hubContext);
        const string text = "inline text";
        var actualHash = await Utility.CalculateSHA256(text, token);
        var dto = new ProfileDto
        {
            Type = ProfileType.Text,
            Hash = omitHash ? string.Empty : actualHash.ToLowerInvariant(),
            Text = text,
            HasData = false,
        };

        var result = await controller.PutSyncProfile(dto, token);

        Assert.IsInstanceOfType<OkResult>(result);
        var entity = await fixture.DbContext.HistoryRecords.SingleAsync(token);
        Assert.AreEqual(text, entity.Text);
        Assert.IsTrue(Utility.SHA256Same(actualHash, entity.Hash));
        Assert.IsNull(entity.TransferDataHash);
        Assert.IsNotNull(hubContext.Client.LastProfile);
        Assert.AreEqual(text, hubContext.Client.LastProfile.Text);
        Assert.IsTrue(Utility.SHA256Same(actualHash, hubContext.Client.LastProfile.Hash));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task PutSyncProfile_AssignsServerSyncTimeToSavedAndPublishedProfile(bool existingProfile, bool suppliedTime)
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await TestFixture.CreateAsync(token);
        var hubContext = new TestHubContext();
        var controller = fixture.Clipboard(hubContext);
        var profile = new TextProfile("sync time");
        if (existingProfile)
        {
            await fixture.Service.AddProfile("default_user", profile, token);
        }
        var dto = await profile.ToProfileDto(token);
        dto.SyncedAt = suppliedTime
            ? new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.FromHours(-7))
            : null;
        var before = DateTimeOffset.Now;

        var result = await controller.PutSyncProfile(dto, token);

        var after = DateTimeOffset.Now;
        Assert.IsInstanceOfType<OkResult>(result);
        var syncedAt = hubContext.Client.LastProfile?.SyncedAt;
        Assert.IsNotNull(syncedAt);
        Assert.IsTrue(syncedAt >= before && syncedAt <= after);
        Assert.AreEqual(TimeZoneInfo.Local.GetUtcOffset(syncedAt.Value), syncedAt.Value.Offset);

        var cachedResult = await controller.GetSyncProfile(token);
        var cached = (ProfileDto)((OkObjectResult)cachedResult.Result!).Value!;
        Assert.IsTrue(syncedAt.Value.EqualsExact(cached.SyncedAt!.Value));

        var restarted = fixture.Clipboard(hubContext);
        var savedResult = await restarted.GetSyncProfile(token);
        var saved = (ProfileDto)((OkObjectResult)savedResult.Result!).Value!;
        Assert.IsTrue(syncedAt.Value.EqualsExact(saved.SyncedAt!.Value));
    }

    [TestMethod]
    public async Task PutSyncProfile_GroupWithMatchingArchiveHashStillRequiresSemanticValidation()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await TestFixture.CreateAsync(token);
        var fileDirectory = Path.Combine(fixture.ServerEnv.GetClipboardDir("default_user"), "file");
        Directory.CreateDirectory(fileDirectory);
        var archivePath = Path.Combine(fileDirectory, "ordinary.zip");
        await CreateArchiveAsync(archivePath, "unexpected.txt", "unexpected", token);
        var transferDataHash = await Utility.CalculateFileSHA256(archivePath, token);
        var controller = fixture.Clipboard(null!);
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
            Path.Combine(fixture.RootDirectory, "ordinary-client"), token))?.Path;
        Assert.IsNotNull(clientArchivePath);
        var dto = await sourceProfile.ToProfileDto(token);
        var fileDirectory = Path.Combine(fixture.ServerEnv.GetClipboardDir("default_user"), "file");
        Directory.CreateDirectory(fileDirectory);
        File.Copy(clientArchivePath, Path.Combine(fileDirectory, dto.DataName!));
        var hubContext = new TestHubContext();
        var controller = fixture.Clipboard(hubContext);

        var result = await controller.PutSyncProfile(dto, token);

        Assert.IsInstanceOfType<OkResult>(result);
        var entity = await fixture.DbContext.HistoryRecords.SingleAsync(token);
        Assert.AreEqual(dto.TransferDataHash, entity.TransferDataHash);
        Assert.AreEqual(dto.TransferDataHash, hubContext.Client.LastProfile?.TransferDataHash);
    }

    [TestMethod]
    public async Task AccountsShareVerifiedFilesAndKeepIndependentClipboardAndHistory()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await TestFixture.CreateAsync(token);
        var source = Path.Combine(fixture.RootDirectory, "shared.txt");
        await File.WriteAllTextAsync(source, "same content", token);
        var group = new GroupProfile([source]);
        var zip = (await group.PrepareTransferData(Path.Combine(fixture.RootDirectory, "client"), token))!;
        var hash = await group.GetHash(token);
        var dto = CreateGroupDto(hash);
        dto.Title = "Alice's title";
        await using (var stream = File.OpenRead(zip.Path))
            await fixture.Service.AddRecordDto("alice", dto, zip.Hash, stream, token);

        var hub = new TestHubContext();
        var bob = fixture.Clipboard(hub, "bob");
        var profile = await group.ToProfileDto(token);
        Assert.IsNull(await fixture.Service.GetByTypeAndHashAsync("bob", group.Type, hash, token));
        Assert.IsInstanceOfType<NotFoundObjectResult>(await bob.PutSyncProfile(profile, token));
        var id = Profile.GetProfileId(group.Type, hash);
        Assert.IsNull(await fixture.Service.GetTransferDataByProfileId("bob", id, token));

        var stage = Directory.CreateDirectory(Path.Combine(fixture.ServerEnv.GetClipboardDir("bob"), "file"));
        var upload = Path.Combine(stage.FullName, profile.DataName!);
        await File.WriteAllTextAsync(upload, "invalid archive", token);
        Assert.IsInstanceOfType<BadRequestObjectResult>(await bob.PutSyncProfile(profile, token));
        Assert.IsNull(await fixture.Service.GetByTypeAndHashAsync("bob", group.Type, hash, token));
        var aliceFile = await fixture.Service.GetTransferDataByProfileId("alice", id, token);
        Assert.IsNotNull(aliceFile);
        Assert.AreEqual(zip.Hash, await Utility.CalculateFileSHA256(aliceFile.Path, token));

        File.Copy(zip.Path, upload, true);
        Assert.IsInstanceOfType<OkResult>(await bob.PutSyncProfile(profile, token));
        var published = (OkObjectResult)(await bob.GetSyncProfile(token)).Result!;
        Assert.IsInstanceOfType<OkResult>(await bob.PutSyncProfile((ProfileDto)published.Value!, token));
        Assert.IsInstanceOfType<OkResult>(await bob.PutSyncProfile(profile with { DataName = null }, token));
        var records = await fixture.DbContext.HistoryRecords.OrderBy(r => r.UserId).ToArrayAsync(token);
        Assert.HasCount(2, records);
        Assert.AreEqual("Alice's title", records[0].Title);
        Assert.IsNull(records[1].Title);
        Assert.AreEqual(records[0].TransferDataFile, records[1].TransferDataFile);
        CollectionAssert.AreEqual(records[0].FilePaths, records[1].FilePaths);
        Assert.IsNull(fixture.ServerEnv.ReadClipboard("alice"));
        Assert.AreEqual(hash, fixture.ServerEnv.ReadClipboard("bob")!.Hash);
        Assert.IsEmpty(stage.GetFiles());

        var transferName = records[0].TransferDataFile;
        await fixture.Service.ClearAllAsync("alice", token);
        Assert.IsNull(await fixture.Service.GetRecentTransferFile("alice", transferName, token));
        Assert.IsNotNull(await fixture.Service.GetTransferDataByProfileId("bob", id, token));
        await fixture.Service.ClearAllAsync("bob", token);
        await fixture.Service.CleanOrphanedFolders(token);
        Assert.IsInstanceOfType<FileContentResult>(await bob.GetFileFromFolder(transferName, token));
        Assert.IsInstanceOfType<FileContentResult>(await bob.GetFileFromFolder(profile.DataName!, token));
        var currentAttachment = await fixture.Service.GetTransferDataByProfileId("bob", id, token);
        Assert.AreEqual(zip.Hash, currentAttachment?.Hash);
        Assert.IsNull(await fixture.Service.GetTransferDataByProfileId("alice", id, token));
        await bob.PutSyncProfile(await new TextProfile("next").ToProfileDto(token), token);
        await fixture.Service.CleanOrphanedFolders(token);
        Assert.IsFalse(Directory.Exists(Profile.QueryGetWorkingDir(fixture.PersistentDirectory, group.Type, hash)));
    }

    [TestMethod]
    public async Task EquivalentGroupsReuseExtractedFilesAndPreserveEachVerifiedArchive()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await TestFixture.CreateAsync(token);
        var source = Path.Combine(fixture.RootDirectory, "source.txt");
        await File.WriteAllTextAsync(source, "identical", token);
        var group = new GroupProfile([source]);
        var zip = (await group.PrepareTransferData(Path.Combine(fixture.RootDirectory, "client"), token))!;
        var dto = CreateGroupDto(await group.GetHash(token));
        await using (var stream = File.OpenRead(zip.Path))
            await fixture.Service.AddRecordDto("alice", dto, zip.Hash, stream, token);
        using (var archive = ZipFile.Open(zip.Path, ZipArchiveMode.Update))
            archive.Comment = "second representation";
        var secondHash = await Utility.CalculateFileSHA256(zip.Path, token);
        await using (var stream = File.OpenRead(zip.Path))
            await fixture.Service.AddRecordDto("bob", dto, secondHash, stream, token);
        var records = await fixture.DbContext.HistoryRecords.OrderBy(r => r.UserId).ToArrayAsync(token);
        CollectionAssert.AreEqual(records[0].FilePaths, records[1].FilePaths);
        var id = Profile.GetProfileId(group.Type, dto.Hash);
        var first = await fixture.Service.GetTransferDataByProfileId("alice", id, token);
        var second = await fixture.Service.GetTransferDataByProfileId("bob", id, token);
        Assert.AreEqual(zip.Hash, first!.Hash);
        Assert.AreEqual(secondHash, second!.Hash);
        await fixture.Service.ClearAllAsync("alice", token);
        Assert.IsFalse(File.Exists(first.Path));
        Assert.IsTrue(File.Exists(second.Path));
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

    private static async Task CreateArchiveAsync(
        string archivePath, string entryName, string content, CancellationToken token)
    {
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry(entryName);
        await using var writer = new StreamWriter(entry.Open());
        await writer.WriteAsync(content.AsMemory(), token);
    }

    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, true)]
    [DataRow(false, false)]
    [DataRow(true, false)]
    public async Task UpdateDeletedRecord_RespectsConflictAndRequiresAttachmentUpload(bool attachment, bool localWins)
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await TestFixture.CreateAsync(token);
        var stamp = DateTime.UtcNow;
        var entity = new HistoryRecordEntity
        {
            UserId = "user",
            Type = attachment ? ProfileType.File : ProfileType.Text,
            Hash = new string('A', 64),
            IsDeleted = true,
            TransferDataFile = attachment ? "removed.txt" : string.Empty,
            Version = 7,
            LastModified = stamp
        };
        fixture.DbContext.HistoryRecords.Add(entity);
        await fixture.DbContext.SaveChangesAsync(token);
        var (updated, server) = await fixture.Service.Update("user", entity.Type, entity.Hash,
            new HistoryRecordUpdateDto
            {
                IsDelete = false,
                Version = localWins ? 8 : 6,
                LastModified = stamp,
                Starred = true
            }, token);
        Assert.AreEqual<bool?>(localWins ? (attachment ? null : true) : false, updated);
        await fixture.DbContext.Entry(entity).ReloadAsync(token);
        Assert.AreEqual(!localWins || attachment, entity.IsDeleted);
        Assert.AreEqual(localWins && !attachment ? 8 : 7, entity.Version);
        if (!localWins)
        {
            Assert.IsNotNull(server);
            Assert.IsTrue(server.IsDeleted);
            Assert.AreEqual(7, server.Version);
        }
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private TestFixture(
            string rootDirectory, string persistentDirectory, HistoryDbContext dbContext, ServerEnvProvider serverEnv,
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

        public SyncClipboardController Clipboard(IHubContext<SyncClipboardHub, ISyncClipboardClient> hub, string id = "default_user")
            => new(hub, ServerEnv, Service) { ControllerContext = new() { HttpContext = TestUserContext.Create(id) } };

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
            var service = new HistoryService(dbContext, serverEnv, null!);
            return new TestFixture(rootDirectory, persistentDirectory, dbContext, serverEnv, service);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            SqliteConnection.ClearAllPools();
            Directory.Delete(RootDirectory, recursive: true);
        }
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
