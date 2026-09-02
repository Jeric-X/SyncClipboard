using System.Text.Json;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Shared;
using SyncClipboard.Shared.Profiles;

namespace SyncClipboard.Test;

[TestClass]
public class ProfileDtoTransferDataHashTests
{
    public TestContext TestContext { get; set; } = null!;

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
            Assert.IsFalse(restored.HasVerifiedTransferDataHashBinding);
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
            Assert.IsFalse(restored.HasVerifiedTransferDataHashBinding);
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
            Assert.IsFalse(restored.HasVerifiedTransferDataHashBinding);
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
            var archivePath = await profile.PrepareTransferData(Path.Combine(testDirectory, "persistent"), token);
            Assert.IsNotNull(archivePath);

            var dto = await profile.ToProfileDto(token);
            var restored = Profile.Create(dto);

            Assert.AreEqual(profile.TransferDataHash, dto.TransferDataHash);
            Assert.AreEqual(profile.TransferDataHash, restored.TransferDataHash);
            Assert.IsFalse(restored.HasVerifiedTransferDataHashBinding);

            await restored.SetTransferData(
                archivePath,
                TransferDataValidation.Full(),
                token);
            Assert.IsTrue(restored.HasVerifiedTransferDataHashBinding);
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
            var archivePath = await sourceProfile.PrepareTransferData(
                Path.Combine(testDirectory, "persistent"),
                token);
            Assert.IsNotNull(archivePath);
            var dto = await sourceProfile.ToProfileDto(token);
            dto.Hash = new string('D', 64);
            var remoteProfile = Profile.Create(dto);

            Assert.IsFalse(remoteProfile.HasVerifiedTransferDataHashBinding);
            await Assert.ThrowsExactlyAsync<InvalidDataException>(
                () => remoteProfile.SetTransferData(
                    archivePath,
                    TransferDataValidation.Full(remoteProfile.TransferDataHash),
                    token));
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task GroupProfileDto_AcceptsVerifiedBindingFromOfficialServer()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var filePath = Path.Combine(testDirectory, "file.txt");
            await File.WriteAllTextAsync(filePath, "group", token);
            var sourceProfile = new GroupProfile([filePath]);
            var archivePath = await sourceProfile.PrepareTransferData(
                Path.Combine(testDirectory, "persistent"),
                token);
            Assert.IsNotNull(archivePath);
            var dto = await sourceProfile.ToProfileDto(token);
            dto.Hash = new string('D', 64);
            var officialProfile = Profile.Create(dto, isTransferDataHashBindingVerified: true);

            Assert.IsTrue(officialProfile.HasVerifiedTransferDataHashBinding);
            await officialProfile.SetTransferData(
                archivePath,
                TransferDataValidation.PreferTransferDataHash(
                    officialProfile.TransferDataHash),
                token);
            var persistentInfo = await officialProfile.Persist(
                Path.Combine(testDirectory, "official-persistent"),
                token);
            Assert.IsTrue(officialProfile.HasVerifiedTransferDataHashBinding);
            Assert.AreEqual(dto.TransferDataHash, persistentInfo.TransferDataHash);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task GroupProfile_DoesNotPersistUnverifiedBinding()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var filePath = Path.Combine(testDirectory, "file.txt");
            await File.WriteAllTextAsync(filePath, "group", token);
            var sourceProfile = new GroupProfile([filePath]);
            var archivePath = await sourceProfile.PrepareTransferData(
                Path.Combine(testDirectory, "source-persistent"),
                token);
            Assert.IsNotNull(archivePath);
            var dto = await sourceProfile.ToProfileDto(token);
            dto.Hash = new string('D', 64);
            var unverifiedProfile = Profile.Create(dto);
            await unverifiedProfile.SetTransferData(
                archivePath,
                TransferDataValidation.Unverified,
                token);

            await Assert.ThrowsExactlyAsync<InvalidDataException>(
                () => unverifiedProfile.Persist(
                    Path.Combine(testDirectory, "unverified-persistent"),
                    token));
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task RemoteHistory_PersistsOnlyVerifiedOrOfficialBinding()
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

        var unverifiedProfile = Profile.Create(dto);
        var officialProfile = Profile.Create(dto, isTransferDataHashBindingVerified: true);

        var unverifiedRecord = await HistoryManager.ToRemoteHistoryRecord(unverifiedProfile, token);
        var officialRecord = await HistoryManager.ToRemoteHistoryRecord(officialProfile, token);

        Assert.IsNull(unverifiedRecord.TransferDataHash);
        Assert.AreEqual(dto.TransferDataHash, officialRecord.TransferDataHash);
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
                () => remoteProfile.SetTransferData(
                    filePath,
                    TransferDataValidation.Full(remoteProfile.TransferDataHash),
                    token));
            Assert.IsFalse(remoteProfile.HasVerifiedTransferDataHashBinding);
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
            var transferPath = await sourceProfile.PrepareTransferData(testDirectory, token);
            Assert.IsNotNull(transferPath);
            var dto = await sourceProfile.ToProfileDto(token);
            dto.TransferDataHash = new string('D', 64);
            var remoteProfile = Profile.Create(dto);

            await Assert.ThrowsExactlyAsync<InvalidDataException>(
                () => remoteProfile.SetTransferData(
                    transferPath,
                    TransferDataValidation.Full(remoteProfile.TransferDataHash),
                    token));
            Assert.IsFalse(remoteProfile.HasVerifiedTransferDataHashBinding);
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
