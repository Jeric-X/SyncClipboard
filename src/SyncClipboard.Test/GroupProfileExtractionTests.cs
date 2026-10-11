using SyncClipboard.Shared.Models;
using SyncClipboard.Shared.Profiles;
using SyncClipboard.Shared.Profiles.Models;
using SyncClipboard.Shared.Utilities;
using System.IO.Compression;

namespace SyncClipboard.Test;

[TestClass]
public class GroupProfileExtractionTests
{
    public TestContext TestContext { get; set; } = null!;
    private CancellationToken Token => TestContext.CancellationTokenSource.Token;

    private sealed class Fixture : IDisposable
    {
        private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("SyncClipboard-GroupExtraction-");
        public string Root => root.FullName;
        public void Dispose() => root.Delete(true);
    }

    [TestMethod]
    [DataRow(false, 5, false)]
    [DataRow(false, 6, true)]
    [DataRow(true, 5, false)]
    [DataRow(true, 6, true)]
    public async Task GroupEntryLimit_CountsImplicitParentsAndEmptyDirectoriesOnce(bool explicitParents, int limit, bool accepted)
    {
        using var f = new Fixture();
        var path = Path.Combine(f.Root, "entries.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            zip.CreateEntry("folder/");
            if (explicitParents)
            {
                zip.CreateEntry("folder/a/");
                zip.CreateEntry("folder/a/b/");
            }
            zip.CreateEntry("folder/a/b/first.txt");
            zip.CreateEntry("folder/a/b/second.txt");
            zip.CreateEntry("folder/empty/");
        }
        var profile = new GroupProfile(new SyncClipboard.Shared.ProfileDto());
        var file = new FileHashInfo(path, await Utility.CalculateFileSHA256(path, Token));
        if (accepted)
        {
            await profile.SetTransferData(file, 0, (uint)limit, Token);
            var folder = profile.Files.Single();
            Assert.IsTrue(File.Exists(Path.Combine(folder, "a", "b", "first.txt")));
            Assert.IsTrue(File.Exists(Path.Combine(folder, "a", "b", "second.txt")));
            Assert.IsTrue(Directory.Exists(Path.Combine(folder, "empty")));
            Assert.AreEqual(0L, await profile.GetSize(Token));
        }
        else
        {
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => profile.SetTransferData(file, 0, (uint)limit, Token));
            Assert.IsEmpty(Directory.GetDirectories(f.Root, "entries.*"));
        }
    }

    [TestMethod]
    [DataRow("folder/../extra")]
    [DataRow("./extra")]
    [DataRow("./declared.txt")]
    [DataRow("folder/./extra")]
    [DataRow("folder/../extra/")]
    public async Task GroupExtraction_RejectsDotSegmentsEvenInsideRoot(string entryName)
    {
        using var f = new Fixture();
        var declared = Path.Combine(f.Root, "declared.txt");
        await File.WriteAllTextAsync(declared, "", Token);
        var hash = await new GroupProfile([declared]).GetHash(Token);
        var path = Path.Combine(f.Root, "group.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            archive.CreateEntry("declared.txt");
            archive.CreateEntry(entryName);
        }
        var profile = new GroupProfile(new SyncClipboard.Shared.ProfileDto { Hash = hash });
        var transfer = new FileHashInfo(path, await Utility.CalculateFileSHA256(path, Token));
        var error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => profile.SetTransferData(transfer, 0, 100, Token));
        Assert.Contains("dot path segment", error.Message);
        Assert.IsEmpty(Directory.GetDirectories(f.Root));
    }

    [TestMethod]
    public async Task GroupExtraction_RejectsUnderstatedEntryLengthWhileStreaming()
    {
        using var f = new Fixture();
        var path = Path.Combine(f.Root, "forged.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            await using var entry = zip.CreateEntry("large.bin", CompressionLevel.Optimal).Open();
            await entry.WriteAsync(new byte[1024 * 1024], Token);
        }
        var bytes = await File.ReadAllBytesAsync(path, Token);
        // Central-directory uncompressed size claims one byte; the deflate stream contains 1 MiB.
        var offset = bytes.AsSpan().IndexOf(new byte[] { 0x50, 0x4b, 0x01, 0x02 });
        Assert.IsGreaterThanOrEqualTo(0, offset);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 24, 4), 1);
        await File.WriteAllBytesAsync(path, bytes, Token);
        var profile = new GroupProfile(new SyncClipboard.Shared.ProfileDto { Hash = new string('0', 64) });
        var hash = await Utility.CalculateFileSHA256(path, Token);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => profile.SetTransferData(new FileHashInfo(path, hash), 1, 1_000_000, Token));
        Assert.IsEmpty(Directory.GetDirectories(f.Root, "forged.*"));
    }

}
