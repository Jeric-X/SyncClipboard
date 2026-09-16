using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Test;

[TestClass]
public class UtilitySHA256Tests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task InvalidHashesAreRejectedInsteadOfTreatedAsMissing()
    {
        string[] hashes = ["", " ", "malformed", new string('A', 63), new string('A', 65), new string('G', 64)];
        foreach (var hash in hashes)
        {
            Assert.ThrowsExactly<ArgumentException>(() => Utility.NormalizeSHA256(hash));
            await Assert.ThrowsExactlyAsync<InvalidDataException>(
                () => Utility.VerifyFileSHA256(string.Empty, hash, TestContext.CancellationToken));
        }
    }

    [TestMethod]
    public async Task MissingHashAndLowercaseHashRemainSupported()
    {
        var token = TestContext.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("SyncClipboard-SHA256-");
        try
        {
            var path = Path.Combine(directory.FullName, "file.txt");
            await File.WriteAllTextAsync(path, "content", token);
            var expectedHash = await Utility.CalculateFileSHA256(path, token);

            Assert.IsNull(Utility.NormalizeSHA256(null));
            Assert.AreEqual(expectedHash, Utility.NormalizeSHA256(expectedHash.ToLowerInvariant()));
            Assert.AreEqual(expectedHash, await Utility.VerifyFileSHA256(path, null, token));
            Assert.AreEqual(expectedHash, await Utility.VerifyFileSHA256(path, expectedHash.ToLowerInvariant(), token));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    public async Task VerifyFileSHA256_RejectsMismatchedHash()
    {
        var token = TestContext.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("SyncClipboard-SHA256-");
        try
        {
            var path = Path.Combine(directory.FullName, "file.txt");
            await File.WriteAllTextAsync(path, "content", token);
            var mismatchedHash = await Utility.CalculateSHA256("different content", token);

            await Assert.ThrowsExactlyAsync<InvalidDataException>(
                () => Utility.VerifyFileSHA256(path, mismatchedHash, token));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    public async Task FileMatchesSHA256ChecksContentAndHandlesUnavailableData()
    {
        var token = TestContext.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("SyncClipboard-ValidateSHA256-");
        try
        {
            var path = Path.Combine(directory.FullName, "file.txt");
            await File.WriteAllTextAsync(path, "content", token);
            var expectedHash = await Utility.CalculateFileSHA256(path, token);

            Assert.IsTrue(await Utility.FileMatchesSHA256(path, expectedHash.ToLowerInvariant(), token));
            Assert.IsFalse(await Utility.FileMatchesSHA256(null, expectedHash, token));
            Assert.IsFalse(await Utility.FileMatchesSHA256(string.Empty, expectedHash, token));
            Assert.IsFalse(await Utility.FileMatchesSHA256(path, null, token));
            Assert.IsFalse(await Utility.FileMatchesSHA256(path, "malformed", token));

            await using (var lockedFile = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.IsFalse(await Utility.FileMatchesSHA256(path, expectedHash, token));
            }

            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => Utility.FileMatchesSHA256(path, expectedHash, canceled.Token));

            await File.WriteAllTextAsync(path, "changed", token);
            Assert.IsFalse(await Utility.FileMatchesSHA256(path, expectedHash, token));

            File.Delete(path);
            Assert.IsFalse(await Utility.FileMatchesSHA256(path, expectedHash, token));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    public void SHA256Same_IgnoresCaseAndDetectsDifferentHashes()
    {
        var hash = new string('A', 64);

        Assert.IsTrue(Utility.SHA256Same(hash, hash));
        Assert.IsTrue(Utility.SHA256Same(hash, hash.ToLowerInvariant()));
        Assert.IsFalse(Utility.SHA256Same(hash, new string('B', 64)));
    }

    [TestMethod]
    [DataRow(null, null, true)]
    [DataRow("", "", true)]
    [DataRow(null, "", false)]
    [DataRow(null, "abc", false)]
    [DataRow("abc", null, false)]
    [DataRow("invalid", "INVALID", true)]
    public void SHA256Same_PreservesStringComparisonSemantics(string? first, string? second, bool expected)
    {
        Assert.AreEqual(expected, Utility.SHA256Same(first, second));
    }
}
