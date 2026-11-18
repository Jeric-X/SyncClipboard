using System.Text.Json;
using SyncClipboard.Server.Core.Models;

namespace SyncClipboard.Test.Server.History;

[TestClass]
public class HistoryTitleDtoTests
{
    [TestMethod]
    [DataRow("{}", null)]
    [DataRow("""{"title":null}""", null)]
    [DataRow("""{"title":""}""", "")]
    [DataRow("""{"title":"应用图标"}""", "应用图标")]
    [DataRow("""{"title":"null"}""", "null")]
    public void Deserialize_HandlesOptionalTitleAcrossHistoryDtos(string json, string? expected)
    {
        var options = JsonSerializerOptions.Web;
        Assert.AreEqual(expected, JsonSerializer.Deserialize<HistoryRecordCreateDto>(json, options)!.Title);
        Assert.AreEqual(expected, JsonSerializer.Deserialize<HistoryRecordDto>(json, options)!.Title);
        Assert.AreEqual(expected, JsonSerializer.Deserialize<HistoryRecordUpdateDto>(json, options)!.Title);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("应用图标")]
    public void EntityTitle_RoundTripsThroughQueryAndUpdateResponse(string? title)
    {
        var record = HistoryRecordDto.FromEntity(new HistoryRecordEntity { Title = title });
        var options = JsonSerializerOptions.Web;
        var query = JsonSerializer.SerializeToElement(record, options);
        Assert.AreEqual(title, query.GetProperty("title").GetString());

        var received = query.Deserialize<HistoryRecordDto>(options)!;
        Assert.AreEqual(title, received.ToEntity("user").Title);
        var response = JsonSerializer.SerializeToElement(received.ToUpdateDto(), options);
        if (title is null)
            Assert.IsFalse(response.TryGetProperty("title", out _));
        else
            Assert.AreEqual(title, response.GetProperty("title").GetString());
    }

    [TestMethod]
    [DataRow("  应用 图标  ", "应用 图标")]
    [DataRow("  ", "")]
    public void ToEntity_TrimsSuppliedTitle(string input, string expected)
    {
        Assert.AreEqual(expected, new HistoryRecordDto { Title = input }.ToEntity("user").Title);
    }
}
