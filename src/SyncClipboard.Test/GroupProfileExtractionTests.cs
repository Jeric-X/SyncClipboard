using SyncClipboard.Shared.Models;
using SyncClipboard.Shared.Profiles;
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
        var error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => profile.SetTransferData(transfer, true, Token));
        Assert.Contains("dot path segment", error.Message);
        Assert.IsEmpty(Directory.GetDirectories(f.Root));
    }
}
