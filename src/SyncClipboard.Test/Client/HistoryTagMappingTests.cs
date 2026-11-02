using SyncClipboard.Core.Models;
using SyncClipboard.Server.Core.Models;

namespace SyncClipboard.Test.Client;

[TestClass]
public class HistoryTagMappingTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void LegacyServerUpdate_PreservesTagsWhileApplyingOtherChanges(bool partial)
    {
        var local = new HistoryRecord { Tags = ["local"], Version = 3 };

        ApplyRemote(local, null, partial);

        string[] expected = ["local"];
        CollectionAssert.AreEqual(expected, local.Tags);
        Assert.IsTrue(local.Stared);
        Assert.AreEqual(4, local.Version);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void RemoteTagReplacement_NormalizesOrClearsTags(bool partial, bool clear)
    {
        var local = new HistoryRecord { Tags = ["old"] };
        string[] incoming = clear ? [] : [" 工作 ", "Work", "work", "e\u0301", "é", "工作"];
        string[] expected = clear ? [] : ["Work", "work", "é", "工作"];

        ApplyRemote(local, incoming, partial);

        CollectionAssert.AreEqual(expected, local.Tags);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void InvalidRemoteTags_LeaveLocalMetadataUnchanged(bool partial)
    {
        var modified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var local = new HistoryRecord { Tags = ["local"], Version = 3, LastModified = modified };

        Assert.ThrowsExactly<ArgumentException>(() => ApplyRemote(local, ["bad\nname"], partial));

        string[] expected = ["local"];
        CollectionAssert.AreEqual(expected, local.Tags);
        Assert.IsFalse(local.Stared);
        Assert.AreEqual(3, local.Version);
        Assert.AreEqual(modified, local.LastModified);
    }

    [TestMethod]
    public void RemoteRecordCreation_NormalizesTagsAndAcceptsLegacyRecords()
    {
        var remote = new HistoryRecordDto { Tags = [" 工作 ", "e\u0301", "é", "工作"] };
        var local = remote.ToHistoryRecord();

        string[] expected = ["é", "工作"];
        CollectionAssert.AreEqual(expected, local.Tags);
        Assert.IsEmpty(new HistoryRecordDto().ToHistoryRecord().Tags);
    }

    [TestMethod]
    public void TransferSnapshots_DoNotShareMutableTags()
    {
        var local = new HistoryRecord { Tags = ["local"] };
        var outgoing = local.ToHistoryRecordDto();
        Assert.IsNotNull(outgoing.Tags);
        local.Tags[0] = "later-local-edit";
        Assert.AreEqual("local", outgoing.Tags.Single());
        outgoing.Tags[0] = "outgoing-edit";
        Assert.AreEqual("later-local-edit", local.Tags.Single());

        var created = outgoing.ToHistoryRecord();
        var updated = new HistoryRecord();
        updated.ApplyChangesFromRemote(outgoing);
        var partialUpdate = new HistoryRecordUpdateDto { Tags = outgoing.Tags };
        var patched = new HistoryRecord();
        patched.ApplyFromServerUpdateDto(partialUpdate);
        outgoing.Tags[0] = "later-remote-edit";

        Assert.AreEqual("outgoing-edit", created.Tags.Single());
        Assert.AreEqual("outgoing-edit", updated.Tags.Single());
        Assert.AreEqual("outgoing-edit", patched.Tags.Single());
    }

    private static void ApplyRemote(HistoryRecord local, string[]? tags, bool partial)
    {
        var modified = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        if (partial)
            local.ApplyFromServerUpdateDto(new HistoryRecordUpdateDto
            {
                Tags = tags,
                Starred = true,
                Version = 4,
                LastModified = modified
            });
        else
            local.ApplyChangesFromRemote(new HistoryRecordDto
            {
                Tags = tags,
                Starred = true,
                Version = 4,
                LastModified = modified
            });
    }
}
