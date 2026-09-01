using System.IO.Compression;
using SyncClipboard.Shared.Models;
using SyncClipboard.Shared.Profiles;
using SyncClipboard.Shared.Profiles.Models;

namespace SyncClipboard.Test;

[TestClass]
public class GroupProfileTransferTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task PrepareTransferData_AllFilesMissing_DoesNotCreateArchive()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var persistentDirectory = Path.Combine(testDirectory, "persistent");
            var missingFile = Path.Combine(testDirectory, "missing.txt");
            var profile = new GroupProfile([missingFile], new string('A', 64));

            await Assert.ThrowsExactlyAsync<LocalProfileDataUnavailableException>(
                () => profile.PrepareTransferData(persistentDirectory, token));

            AssertNoTransferFiles(persistentDirectory);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task PrepareTransferData_OneFileMissing_DoesNotCreatePartialArchive()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var persistentDirectory = Path.Combine(testDirectory, "persistent");
            var existingFile = Path.Combine(testDirectory, "existing.txt");
            var missingFile = Path.Combine(testDirectory, "missing.txt");
            await File.WriteAllTextAsync(existingFile, "existing", token);
            var profile = new GroupProfile([existingFile, missingFile], new string('A', 64));

            await Assert.ThrowsExactlyAsync<LocalProfileDataUnavailableException>(
                () => profile.PrepareTransferData(persistentDirectory, token));

            AssertNoTransferFiles(persistentDirectory);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task PrepareTransferData_FileChangedAfterHash_DoesNotCreateArchive()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var persistentDirectory = Path.Combine(testDirectory, "persistent");
            var file = Path.Combine(testDirectory, "changed.txt");
            await File.WriteAllTextAsync(file, "before", token);
            var profile = new GroupProfile([file]);
            await profile.GetHash(token);
            await File.WriteAllTextAsync(file, "after", token);

            await Assert.ThrowsExactlyAsync<LocalProfileDataUnavailableException>(
                () => profile.PrepareTransferData(persistentDirectory, token));

            AssertNoTransferFiles(persistentDirectory);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task PrepareTransferData_ArchiveHashDiffersFromProfile_DoesNotPublishArchive()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var persistentDirectory = Path.Combine(testDirectory, "persistent");
            var firstDirectory = Directory.CreateDirectory(Path.Combine(testDirectory, "first"));
            var secondDirectory = Directory.CreateDirectory(Path.Combine(testDirectory, "second"));
            var firstFile = Path.Combine(firstDirectory.FullName, "first.txt");
            var secondFile = Path.Combine(secondDirectory.FullName, "second.txt");
            await File.WriteAllTextAsync(firstFile, "first", token);
            await File.WriteAllTextAsync(secondFile, "second", token);
            var profile = new GroupProfile([firstFile, secondFile]);
            await profile.GetHash(token);

            await Assert.ThrowsExactlyAsync<LocalProfileDataUnavailableException>(
                () => profile.PrepareTransferData(persistentDirectory, token));

            AssertNoTransferFiles(persistentDirectory);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task PrepareTransferData_AllEntriesFiltered_DoesNotCreateEmptyArchive()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var persistentDirectory = Path.Combine(testDirectory, "persistent");
            var file = Path.Combine(testDirectory, "filtered.txt");
            await File.WriteAllTextAsync(file, "filtered", token);
            var profile = new GroupProfile(
                [file],
                new FileFilterConfig { FileFilterMode = "WhiteList" });

            await Assert.ThrowsExactlyAsync<LocalProfileDataUnavailableException>(
                () => profile.PrepareTransferData(persistentDirectory, token));

            AssertNoTransferFiles(persistentDirectory);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task PrepareTransferData_EmptyDirectory_CreatesDirectoryEntry()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var persistentDirectory = Path.Combine(testDirectory, "persistent");
            var emptyDirectory = Directory.CreateDirectory(Path.Combine(testDirectory, "empty"));
            var profile = new GroupProfile([emptyDirectory.FullName]);

            var archivePath = await profile.PrepareTransferData(persistentDirectory, token);

            Assert.IsNotNull(archivePath);
            using var archive = ZipFile.OpenRead(archivePath);
            Assert.HasCount(1, archive.Entries);
            Assert.AreEqual("empty/", archive.Entries[0].FullName);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task PrepareTransferData_ZeroLengthFile_CreatesFileEntry()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var persistentDirectory = Path.Combine(testDirectory, "persistent");
            var emptyFile = Path.Combine(testDirectory, "empty.txt");
            await File.WriteAllBytesAsync(emptyFile, [], token);
            var profile = new GroupProfile([emptyFile]);

            var archivePath = await profile.PrepareTransferData(persistentDirectory, token);

            Assert.IsNotNull(archivePath);
            using var archive = ZipFile.OpenRead(archivePath);
            Assert.HasCount(1, archive.Entries);
            Assert.AreEqual("empty.txt", archive.Entries[0].FullName);
            Assert.AreEqual(0, archive.Entries[0].Length);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task SetTransferData_EmptyArchiveWithEmptyHash_IsRejectedAndExtractionIsCleaned()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var archivePath = Path.Combine(testDirectory, "empty.zip");
            using (ZipFile.Open(archivePath, ZipArchiveMode.Create))
            { }
            var profile = new GroupProfile(
                [],
                "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855");

            await Assert.ThrowsExactlyAsync<InvalidDataException>(
                () => profile.SetTransferData(archivePath, verify: true, token));

            Assert.IsFalse(Directory.Exists(Path.Combine(testDirectory, "empty")));
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task PrepareTransferData_EmptyCachedArchiveIsNotReused()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var archivePath = Path.Combine(testDirectory, "empty.zip");
            using (ZipFile.Open(archivePath, ZipArchiveMode.Create))
            { }
            var profile = new GroupProfile(
                [],
                "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
                archivePath);

            await Assert.ThrowsExactlyAsync<LocalProfileDataUnavailableException>(
                () => profile.PrepareTransferData(Path.Combine(testDirectory, "persistent"), token));
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task PrepareTransferData_NonEmptyCachedArchiveWithMismatchedHashThrows()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var archivePath = Path.Combine(testDirectory, "unverified.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("unexpected.txt");
                await using var writer = new StreamWriter(entry.Open());
                await writer.WriteAsync("unexpected");
            }
            var profile = new GroupProfile([], new string('A', 64), archivePath);

            await Assert.ThrowsExactlyAsync<LocalProfileDataUnavailableException>(
                () => profile.PrepareTransferData(Path.Combine(testDirectory, "persistent"), token));
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task PrepareTransferData_PersistentCachedArchiveWithMismatchedHashThrows()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var archivePath = Path.Combine(testDirectory, "persistent.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("unexpected.txt");
                await using var writer = new StreamWriter(entry.Open());
                await writer.WriteAsync("unexpected");
            }
            var profile = new GroupProfile(new ProfilePersistentInfo
            {
                Type = ProfileType.Group,
                Text = "unexpected.txt",
                Size = 10,
                Hash = new string('A', 64),
                TransferDataFile = archivePath,
            });

            await Assert.ThrowsExactlyAsync<LocalProfileDataUnavailableException>(
                () => profile.PrepareTransferData(Path.Combine(testDirectory, "persistent"), token));
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task IsLocalDataValid_NonEmptyCachedArchiveWithoutSourceFiles_ReturnsFalse()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var archivePath = Path.Combine(testDirectory, "cached.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("cached.txt");
                await using var writer = new StreamWriter(entry.Open());
                await writer.WriteAsync("cached");
            }
            var profile = new GroupProfile([], new string('A', 64), archivePath);

            Assert.IsFalse(await profile.IsLocalDataValid(true, token));
            Assert.IsFalse(await profile.IsLocalDataValid(false, token));
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task PrepareTransferData_TrustedCachedArchiveIsReused()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var persistentDirectory = Path.Combine(testDirectory, "persistent");
            var file = Path.Combine(testDirectory, "source.txt");
            await File.WriteAllTextAsync(file, "source", token);
            var sourceProfile = new GroupProfile([file]);
            var archivePath = await sourceProfile.PrepareTransferData(persistentDirectory, token);
            Assert.IsNotNull(archivePath);

            var cachedProfile = new GroupProfile([file], await sourceProfile.GetHash(token));
            await cachedProfile.SetTransferData(archivePath, verify: false, token);
            File.Delete(file);

            var reusedPath = await cachedProfile.PrepareTransferData(persistentDirectory, token);

            Assert.AreEqual(archivePath, reusedPath);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task PrepareTransferData_VerifiedCachedArchiveChangedAfterSetRegeneratesFromFiles()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var persistentDirectory = Path.Combine(testDirectory, "persistent");
            var file = Path.Combine(testDirectory, "source.txt");
            await File.WriteAllTextAsync(file, "source", token);
            var sourceProfile = new GroupProfile([file]);
            var expectedHash = await sourceProfile.GetHash(token);
            var archivePath = await sourceProfile.PrepareTransferData(persistentDirectory, token);
            Assert.IsNotNull(archivePath);

            var cachedProfile = new GroupProfile([file], expectedHash);
            await cachedProfile.SetTransferData(archivePath, verify: true, token);

            File.Delete(archivePath);
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("source.txt");
                await using var writer = new StreamWriter(entry.Open());
                await writer.WriteAsync("changed");
            }

            var regeneratedPath = await cachedProfile.PrepareTransferData(persistentDirectory, token);

            Assert.IsNotNull(regeneratedPath);
            Assert.AreNotEqual(archivePath, regeneratedPath);
            var verifiedProfile = new GroupProfile([], expectedHash);
            await verifiedProfile.SetTransferData(regeneratedPath, verify: true, token);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task PrepareTransferData_MalformedCachedArchiveRegeneratesFromFiles()
    {
        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        try
        {
            var persistentDirectory = Path.Combine(testDirectory, "persistent");
            var file = Path.Combine(testDirectory, "source.txt");
            await File.WriteAllTextAsync(file, "source", token);
            var expectedHash = await new GroupProfile([file]).GetHash(token);
            var archivePath = Path.Combine(testDirectory, "malformed.zip");
            await File.WriteAllTextAsync(archivePath, "not a zip archive", token);
            var profile = new GroupProfile([file], expectedHash, archivePath);

            var regeneratedPath = await profile.PrepareTransferData(persistentDirectory, token);

            Assert.IsNotNull(regeneratedPath);
            Assert.AreNotEqual(archivePath, regeneratedPath);
            var verifiedProfile = new GroupProfile([], expectedHash);
            await verifiedProfile.SetTransferData(regeneratedPath, verify: true, token);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task PrepareTransferData_OutputFailureIsNotReportedAsLocalDataUnavailable()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
        {
            return;
        }

        var token = TestContext.CancellationTokenSource.Token;
        var testDirectory = CreateTestDirectory();
        var workingDirectory = string.Empty;
        UnixFileMode originalMode = default;
        try
        {
            var persistentDirectory = Path.Combine(testDirectory, "persistent");
            var file = Path.Combine(testDirectory, "source.txt");
            await File.WriteAllTextAsync(file, "source", token);
            var profile = new GroupProfile([file]);
            var expectedHash = await profile.GetHash(token);
            workingDirectory = Profile.CreateWorkingDir(persistentDirectory, ProfileType.Group, expectedHash);
            originalMode = File.GetUnixFileMode(workingDirectory);
            File.SetUnixFileMode(workingDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            try
            {
                await profile.PrepareTransferData(persistentDirectory, token);
                Assert.Fail("Expected archive output creation to fail.");
            }
            catch (LocalProfileDataUnavailableException ex)
            {
                Assert.Fail($"Output failure must remain retryable: {ex}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        finally
        {
            if (!string.IsNullOrEmpty(workingDirectory) && Directory.Exists(workingDirectory))
            {
                File.SetUnixFileMode(workingDirectory, originalMode);
            }
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SyncClipboard-GroupProfileTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void AssertNoTransferFiles(string persistentDirectory)
    {
        if (!Directory.Exists(persistentDirectory))
        {
            return;
        }

        Assert.IsEmpty(Directory.EnumerateFiles(persistentDirectory, "*", SearchOption.AllDirectories));
    }
}
