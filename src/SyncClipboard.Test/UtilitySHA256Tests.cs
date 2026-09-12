using SyncClipboard.Shared.Utilities;

namespace SyncClipboard.Test;

[TestClass]
public class UtilitySHA256Tests
{
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
