using SyncClipboard.Shared;
using System.Text.Json;

namespace SyncClipboard.Test;

[TestClass]
public class ProfileDtoTests
{
    [TestMethod]
    [DataRow("{}")]
    [DataRow("{\"syncedAt\":null}")]
    public void Deserialize_UnknownSyncTimeRemainsNull(string json)
    {
        var dto = JsonSerializer.Deserialize<ProfileDto>(json, JsonSerializerOptions.Web);

        Assert.IsNotNull(dto);
        Assert.IsNull(dto.SyncedAt);
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(dto, JsonSerializerOptions.Web));
        Assert.IsFalse(serialized.RootElement.TryGetProperty("syncedAt", out _));
    }

    [TestMethod]
    [DataRow(480)]
    [DataRow(-210)]
    public void Serialize_SyncTimePreservesInstantAndOffset(int offsetMinutes)
    {
        var syncedAt = new DateTimeOffset(2026, 10, 9, 15, 30, 0, TimeSpan.FromMinutes(offsetMinutes));
        var json = JsonSerializer.Serialize(new ProfileDto { SyncedAt = syncedAt }, JsonSerializerOptions.Web);
        var restored = JsonSerializer.Deserialize<ProfileDto>(json, JsonSerializerOptions.Web);

        Assert.IsNotNull(restored?.SyncedAt);
        Assert.IsTrue(syncedAt.EqualsExact(restored.SyncedAt.Value));
    }

    [TestMethod]
    public void Serialize_NullSizeOmitsProperty()
    {
        var json = JsonSerializer.Serialize(
            new ProfileDto { Size = null },
            JsonSerializerOptions.Web);

        Assert.IsFalse(json.Contains("\"size\"", StringComparison.Ordinal));
        var legacyDto = JsonSerializer.Deserialize<LegacyProfileDto>(json, JsonSerializerOptions.Web);
        Assert.AreEqual(0, legacyDto?.Size);
    }

    [TestMethod]
    public void Serialize_ZeroSizePreservesProperty()
    {
        var json = JsonSerializer.Serialize(
            new ProfileDto { Size = 0 },
            JsonSerializerOptions.Web);

        Assert.Contains("\"size\":0", json);
    }

    private sealed class LegacyProfileDto
    {
        public long Size { get; set; }
    }
}
