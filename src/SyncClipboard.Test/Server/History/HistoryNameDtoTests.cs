using System.Text.Json;
using SyncClipboard.Server.Core.Models;

namespace SyncClipboard.Test.Server.History;

[TestClass]
public class HistoryNameDtoTests
{
    [TestMethod]
    [DataRow("{}", null)]
    [DataRow("""{"name":null}""", null)]
    [DataRow("""{"name":""}""", "")]
    [DataRow("""{"name":"应用图标"}""", "应用图标")]
    [DataRow("""{"name":"null"}""", "null")]
    public void Deserialize_HandlesOptionalNameAcrossHistoryDtos(string json, string? expected)
    {
        var options = JsonSerializerOptions.Web;
        Assert.AreEqual(expected, JsonSerializer.Deserialize<HistoryRecordCreateDto>(json, options)!.Name);
        Assert.AreEqual(expected, JsonSerializer.Deserialize<HistoryRecordDto>(json, options)!.Name);
        Assert.AreEqual(expected, JsonSerializer.Deserialize<HistoryRecordUpdateDto>(json, options)!.Name);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("应用图标")]
    public void EntityName_RoundTripsThroughQueryAndUpdateResponse(string? name)
    {
        var record = HistoryRecordDto.FromEntity(new HistoryRecordEntity { Name = name });
        var options = JsonSerializerOptions.Web;
        var query = JsonSerializer.SerializeToElement(record, options);
        Assert.AreEqual(name, query.GetProperty("name").GetString());

        var received = query.Deserialize<HistoryRecordDto>(options)!;
        Assert.AreEqual(name, received.ToEntity("user").Name);
        var response = JsonSerializer.SerializeToElement(received.ToUpdateDto(), options);
        if (name is null)
            Assert.IsFalse(response.TryGetProperty("name", out _));
        else
            Assert.AreEqual(name, response.GetProperty("name").GetString());
    }

    [TestMethod]
    [DataRow("  应用 图标  ", "应用 图标")]
    [DataRow("  ", "")]
    public void ToEntity_TrimsSuppliedName(string input, string expected)
    {
        Assert.AreEqual(expected, new HistoryRecordDto { Name = input }.ToEntity("user").Name);
    }
}
