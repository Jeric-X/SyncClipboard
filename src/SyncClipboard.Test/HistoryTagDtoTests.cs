using System.Text.Json;
using SyncClipboard.Server.Core.Models;

namespace SyncClipboard.Test;

[TestClass]
public class HistoryTagDtoTests
{
    [TestMethod]
    [DataRow("{}", true)]
    [DataRow("{\"tags\":null}", true)]
    [DataRow("{\"tags\":[]}", false)]
    public void Deserialize_PreservesMissingVersusExplicitEmptyTags(string json, bool unknown)
    {
        var create = JsonSerializer.Deserialize<HistoryRecordCreateDto>(json, JsonSerializerOptions.Web)!;
        var update = JsonSerializer.Deserialize<HistoryRecordUpdateDto>(json, JsonSerializerOptions.Web)!;
        var record = JsonSerializer.Deserialize<HistoryRecordDto>(json, JsonSerializerOptions.Web)!;

        if (unknown)
        {
            Assert.IsNull(create.Tags);
            Assert.IsNull(update.Tags);
            Assert.IsNull(record.Tags);
            Assert.IsNull(record.ToUpdateDto().Tags);
        }
        else
        {
            Assert.IsNotNull(create.Tags);
            Assert.IsEmpty(create.Tags);
            Assert.IsNotNull(update.Tags);
            Assert.IsEmpty(update.Tags);
            Assert.IsNotNull(record.Tags);
            Assert.IsEmpty(record.Tags);
            Assert.IsEmpty(record.ToUpdateDto().Tags!);
        }

        Assert.IsEmpty(record.ToEntity("user").Tags);
    }

    [TestMethod]
    public void UpdateSerialization_OmitsUnknownTagsButWritesExplicitClear()
    {
        var unknown = JsonSerializer.SerializeToElement(new HistoryRecordDto().ToUpdateDto(), JsonSerializerOptions.Web);
        var clear = JsonSerializer.SerializeToElement(
            new HistoryRecordDto { Tags = [] }.ToUpdateDto(), JsonSerializerOptions.Web);

        Assert.IsFalse(unknown.TryGetProperty("tags", out _));
        Assert.AreEqual(JsonValueKind.Array, clear.GetProperty("tags").ValueKind);
        Assert.AreEqual(0, clear.GetProperty("tags").GetArrayLength());
    }

    [TestMethod]
    public void EntityMappingsAndUpdateMapping_DoNotShareMutableTagArrays()
    {
        var entity = new HistoryRecordEntity { Tags = ["工作"] };
        var record = HistoryRecordDto.FromEntity(entity);
        var restored = record.ToEntity("user");
        var update = record.ToUpdateDto();

        record.Tags![0] = "changed";
        Assert.AreEqual("工作", entity.Tags[0]);
        Assert.AreEqual("工作", restored.Tags[0]);
        Assert.AreEqual("工作", update.Tags![0]);

        entity.Tags[0] = "entity-only";
        Assert.AreEqual("changed", record.Tags[0]);
        Assert.AreEqual("工作", restored.Tags[0]);
        Assert.AreEqual("工作", update.Tags[0]);
    }

    [TestMethod]
    public void FromEntity_ReportsKnownEmptyTags()
    {
        var record = HistoryRecordDto.FromEntity(new HistoryRecordEntity());
        Assert.IsNotNull(record.Tags);
        Assert.IsEmpty(record.Tags);
        var json = JsonSerializer.SerializeToElement(record, JsonSerializerOptions.Web);
        Assert.AreEqual(JsonValueKind.Array, json.GetProperty("tags").ValueKind);
        Assert.AreEqual(0, json.GetProperty("tags").GetArrayLength());
    }

    [TestMethod]
    public void ToEntity_NormalizesTagsWithoutMutatingIncomingDto()
    {
        string[] original = [" 工作 ", "e\u0301", "é", "Work", "work"];
        var record = new HistoryRecordDto { Tags = [.. original] };
        var entity = record.ToEntity("user");
        string[] expected = ["Work", "work", "é", "工作"];

        CollectionAssert.AreEqual(expected, entity.Tags);
        CollectionAssert.AreEqual(original, record.Tags);
        Assert.AreEqual("user", entity.UserId);
    }

    [TestMethod]
    public void ToEntity_RejectsInvalidNamesAndExcessiveTagCount()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new HistoryRecordDto { Tags = [" "] }.ToEntity("user"));
        Assert.ThrowsExactly<ArgumentException>(() => new HistoryRecordDto { Tags = ["bad\nname"] }.ToEntity("user"));
        Assert.ThrowsExactly<ArgumentException>(() => new HistoryRecordDto
        {
            Tags = Enumerable.Range(0, 33).Select(i => $"tag{i}").ToArray()
        }.ToEntity("user"));
    }
}
