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
    [DataRow(ProfileType.File)]
    [DataRow(ProfileType.Image)]
    [DataRow(ProfileType.Text)]
    [DataRow(ProfileType.Group)]
    public async Task ProfileDto_PreservesTransferDataHashWithoutLocalData(ProfileType type)
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var filePath = Path.Combine(testDirectory, "file.txt");
            await File.WriteAllTextAsync(filePath, "content", token);
            Profile profile = type switch
            {
                ProfileType.File => new FileProfile(filePath),
                ProfileType.Image => new ImageProfile(filePath),
                ProfileType.Text => new TextProfile(new string('T', 10241)),
                _ => new GroupProfile([filePath]),
            };
            await profile.PrepareTransferData(testDirectory, token);

            var dto = await profile.ToProfileDto(token);
            var restored = Profile.Create(dto);

            Assert.AreEqual(type, dto.Type);
            Assert.IsNotNull(profile.TransferDataHash);
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
    public async Task GroupProfile_SetTransferDataRejectsMismatchedProfileHashWhenVerificationEnabled()
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
    public async Task GroupProfile_SetTransferDataAcceptsBindingWhenVerificationDisabled()
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

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SyncClipboard-ProfileDtoTransferDataHashTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
