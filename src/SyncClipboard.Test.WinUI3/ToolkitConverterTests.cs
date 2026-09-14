using CommunityToolkit.WinUI.Converters;

namespace SyncClipboard.Test.WinUI3;

[TestClass]
public class ToolkitConverterTests
{
    [TestMethod]
    [TestCategory("NonUI")]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public void BoolNegation_ConvertsBothDirections(bool value, bool expected)
    {
        var converter = new BoolNegationConverter();

        Assert.AreEqual(expected, (bool)converter.Convert(value, typeof(bool), null!, "zh-CN"));
        Assert.AreEqual(expected, (bool)converter.ConvertBack(value, typeof(bool), null!, "zh-CN"));
    }
}
