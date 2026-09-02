using SyncClipboard.Shared;
using System.Text.Json;

namespace SyncClipboard.Test;

[TestClass]
public class ProfileDtoTests
{
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
