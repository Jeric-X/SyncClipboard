using System.Text.Json;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Shared;
using SyncClipboard.Shared.Models;
using SyncClipboard.Shared.Profiles;
using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Test;

[TestClass]
public class ProfileDtoTransferDataHashTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(ProfileType.File)]
    [DataRow(ProfileType.Image)]
    [DataRow(ProfileType.Text)]
    [DataRow(ProfileType.Group)]
    public async Task ConstructionAndDtoConversionPreserveOriginalTransferDataHash(ProfileType type)
    {
        string?[] hashes = [null, "", " ", "malformed", new string('A', 63), new string('A', 65),
            new string('G', 64), new string('a', 64)];
        foreach (var hash in hashes)
        {
            var dto = new ProfileDto
            {
                Type = type,
                Text = "source.txt",
                DataName = "data.bin",
                HasData = true,
                Hash = new string('B', 64),
                Size = 10,
                TransferDataHash = hash
            };

            var profile = Profile.Create(dto);
            Assert.AreEqual(hash, profile.TransferDataHash);
            var restoredDto = await profile.ToProfileDto(TestContext.CancellationTokenSource.Token);
            Assert.AreEqual(hash, restoredDto.TransferDataHash);
        }
    }

    [TestMethod]
    public async Task InlineTextDtoOmitsTransferDataHash()
    {
        var profile = new TextProfile(new ProfileDto
        {
            Text = "inline",
            HasData = false,
            TransferDataHash = new string('A', 64)
        });

        var dto = await profile.ToProfileDto(TestContext.CancellationTokenSource.Token);

        Assert.IsFalse(dto.HasData);
        Assert.IsNull(dto.TransferDataHash);
    }

    [TestMethod]
    public void MissingTransferDataHash_DeserializesAsNullAndNullIsOmitted()
    {
        const string oldJson = """
            {"type":"Text","hash":"ABC","text":"text","hasData":false,"size":4}
            """;

        var dto = JsonSerializer.Deserialize<ProfileDto>(oldJson, JsonSerializerOptions.Web);
        Assert.IsNotNull(dto);
        Assert.IsNull(dto.TransferDataHash);

        var serialized = JsonSerializer.Serialize(dto, JsonSerializerOptions.Web);
        Assert.IsFalse(serialized.Contains("transferDataHash", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task FileProfileDto_PreservesTransferDataHash()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var filePath = Path.Combine(testDirectory, "file.txt");
            await File.WriteAllTextAsync(filePath, "file", token);
            var profile = new FileProfile(filePath);
            await profile.PrepareTransferData(testDirectory, token);

            var dto = await profile.ToProfileDto(token);
            var restored = Profile.Create(dto);

            Assert.AreEqual(profile.TransferDataHash, dto.TransferDataHash);
            Assert.AreEqual(profile.TransferDataHash, restored.TransferDataHash);
            Assert.IsFalse(await restored.IsDataComplete(false, token));
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task ImageProfileDto_PreservesTransferDataHash()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var filePath = Path.Combine(testDirectory, "image.png");
            await File.WriteAllBytesAsync(filePath, [1, 2, 3, 4], token);
            var profile = new ImageProfile(filePath);
            await profile.PrepareTransferData(testDirectory, token);

            var dto = await profile.ToProfileDto(token);
            var restored = Profile.Create(dto);

            Assert.AreEqual(ProfileType.Image, dto.Type);
            Assert.AreEqual(profile.TransferDataHash, dto.TransferDataHash);
            Assert.AreEqual(profile.TransferDataHash, restored.TransferDataHash);
            Assert.IsFalse(await restored.IsDataComplete(false, token));
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task LongTextProfileDto_PreservesTransferDataHash()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var profile = new TextProfile(new string('T', 10241));
            await profile.PrepareTransferData(testDirectory, token);

            var dto = await profile.ToProfileDto(token);
            var restored = Profile.Create(dto);

            Assert.AreEqual(profile.TransferDataHash, dto.TransferDataHash);
            Assert.AreEqual(profile.TransferDataHash, restored.TransferDataHash);
            Assert.IsFalse(await restored.IsDataComplete(false, token));
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task GroupProfileDto_PreservesTransferDataHash()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var filePath = Path.Combine(testDirectory, "file.txt");
            await File.WriteAllTextAsync(filePath, "group", token);
            var profile = new GroupProfile([filePath]);
            var archivePath = (await profile.PrepareTransferData(Path.Combine(testDirectory, "persistent"), token))?.Path;
            Assert.IsNotNull(archivePath);

            var dto = await profile.ToProfileDto(token);
            var restored = Profile.Create(dto);

            Assert.AreEqual(profile.TransferDataHash, dto.TransferDataHash);
            Assert.AreEqual(profile.TransferDataHash, restored.TransferDataHash);
            Assert.IsFalse(await restored.IsDataComplete(false, token));

            await restored.SetTransferData(archivePath, verify: true, token);
            Assert.IsTrue(await Utility.FileMatchesSHA256(archivePath, restored.TransferDataHash, token));
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task GroupProfileDto_DoesNotVerifyRemoteHashBinding()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var filePath = Path.Combine(testDirectory, "file.txt");
            await File.WriteAllTextAsync(filePath, "group", token);
            var sourceProfile = new GroupProfile([filePath]);
            var archivePath = (await sourceProfile.PrepareTransferData(
                Path.Combine(testDirectory, "persistent"), token))?.Path;
            Assert.IsNotNull(archivePath);
            var dto = await sourceProfile.ToProfileDto(token);
            dto.Hash = new string('D', 64);
            var remoteProfile = Profile.Create(dto);

            await Assert.ThrowsExactlyAsync<InvalidDataException>(
                () => remoteProfile.SetTransferData(
                    new FileHashInfo(archivePath, remoteProfile.TransferDataHash!), true, token));
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task GroupProfileDto_AcceptsExternallyVerifiedBindingFromOfficialServer()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var filePath = Path.Combine(testDirectory, "file.txt");
            await File.WriteAllTextAsync(filePath, "group", token);
            var sourceProfile = new GroupProfile([filePath]);
            var archivePath = (await sourceProfile.PrepareTransferData(
                Path.Combine(testDirectory, "persistent"), token))?.Path;
            Assert.IsNotNull(archivePath);
            var dto = await sourceProfile.ToProfileDto(token);
            dto.Hash = new string('D', 64);
            var officialProfile = Profile.Create(dto);

            var actualTransferDataHash = await Utility.VerifyFileSHA256(
                archivePath, officialProfile.TransferDataHash, token);
            await officialProfile.SetTransferData(new FileHashInfo(archivePath, actualTransferDataHash), false, token);
            var persistentInfo = await officialProfile.Persist(
                Path.Combine(testDirectory, "official-persistent"), token);
            Assert.IsTrue(await officialProfile.IsDataComplete(false, token));
            Assert.AreEqual(dto.TransferDataHash, persistentInfo.TransferDataHash);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task GroupProfile_PersistDoesNotValidateOrExtractTransferData()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var filePath = Path.Combine(testDirectory, "file.txt");
            await File.WriteAllTextAsync(filePath, "group", token);
            var sourceProfile = new GroupProfile([filePath]);
            var archivePath = (await sourceProfile.PrepareTransferData(
                Path.Combine(testDirectory, "source-persistent"), token))?.Path;
            Assert.IsNotNull(archivePath);
            var dto = await sourceProfile.ToProfileDto(token);
            dto.Hash = new string('D', 64);
            var unverifiedProfile = Profile.Create(dto);
            await unverifiedProfile.SetTransferData(archivePath, verify: false, token);

            var persistentInfo = await unverifiedProfile.Persist(
                Path.Combine(testDirectory, "unverified-persistent"), token);

            Assert.AreEqual(dto.Hash, persistentInfo.Hash);
            Assert.IsNotNull(persistentInfo.TransferDataFile);
            Assert.IsNull(persistentInfo.TransferDataHash);
            Assert.IsEmpty(persistentInfo.FilePaths);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task RemoteHistoryMetadata_DoesNotPublishTransferDataHashBinding()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var dto = new ProfileDto
        {
            Type = ProfileType.Group,
            Hash = new string('A', 64),
            Text = "file.txt",
            HasData = true,
            DataName = "group.zip",
            TransferDataHash = new string('B', 64),
            Size = 1,
        };

        var profile = Profile.Create(dto);

        var record = await HistoryManager.ToRemoteHistoryRecord(profile, token);

        Assert.IsNull(record.TransferDataHash);
    }

    [TestMethod]
    public async Task FileProfileDto_RejectsIncorrectRemoteTransferDataHash()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var filePath = Path.Combine(testDirectory, "file.txt");
            await File.WriteAllTextAsync(filePath, "file", token);
            var sourceProfile = new FileProfile(filePath);
            var dto = await sourceProfile.ToProfileDto(token);
            dto.TransferDataHash = new string('D', 64);
            var remoteProfile = Profile.Create(dto);

            await Assert.ThrowsExactlyAsync<InvalidDataException>(
                () => Utility.VerifyFileSHA256(filePath, remoteProfile.TransferDataHash, token));
            Assert.IsFalse(await remoteProfile.IsDataComplete(false, token));
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task TextProfileDto_RejectsIncorrectRemoteTransferDataHash()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var sourceProfile = new TextProfile(new string('T', 10241));
            var transferPath = (await sourceProfile.PrepareTransferData(testDirectory, token))?.Path;
            Assert.IsNotNull(transferPath);
            var dto = await sourceProfile.ToProfileDto(token);
            dto.TransferDataHash = new string('D', 64);
            var remoteProfile = Profile.Create(dto);

            await Assert.ThrowsExactlyAsync<InvalidDataException>(
                () => Utility.VerifyFileSHA256(transferPath, remoteProfile.TransferDataHash, token));
            Assert.IsFalse(await remoteProfile.IsDataComplete(false, token));
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SyncClipboard-ProfileDtoTransferDataHashTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
