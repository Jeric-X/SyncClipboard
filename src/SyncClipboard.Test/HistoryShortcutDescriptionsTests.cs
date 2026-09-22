using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using System.Globalization;

namespace SyncClipboard.Test;

[TestClass]
[DoNotParallelize]
public class HistoryShortcutDescriptionsTests
{
    [TestMethod]
    [DataRow("en")]
    [DataRow("zh-CN")]
    public void Instructions_ReflectReassignedAndClearedShortcutsAndMouseActions(string culture)
    {
        var previousCulture = Strings.Culture;
        try
        {
            Strings.Culture = CultureInfo.GetCultureInfo(culture);
            var config = new HistoryShortcutConfig
            {
                Shortcuts = new()
                {
                    [HistoryShortcutAction.Search] = new Hotkey(Key.F2),
                    [HistoryShortcutAction.Copy] = Hotkey.Nothing,
                    [HistoryShortcutAction.ExecuteRecommendedAction] = new Hotkey(Key.F3)
                },
                DoubleClickAction = HistoryMouseAction.ExecuteRecommendedAction,
                MiddleClickAction = HistoryMouseAction.Copy
            };

            var instructions = HistoryShortcutDescriptions.BuildInstructions(config);
            Assert.Contains(string.Format(Strings.HistoryWindowOperationFormat, "F2", Strings.HistoryShortcutSearch), instructions);
            Assert.Contains(string.Format(Strings.HistoryWindowOperationFormat, "F3", Strings.ExecuteRecommendedAction), instructions);
            Assert.IsFalse(instructions.Contains("Ctrl+F", StringComparison.Ordinal));
            Assert.IsFalse(instructions.Contains("Alt+Enter", StringComparison.Ordinal));
            Assert.IsFalse(instructions.Contains("Option+Enter", StringComparison.Ordinal));
            Assert.Contains(string.Format(Strings.HistoryWindowMouseList,
                Strings.ExecuteRecommendedAction, Strings.HistoryShortcutCopy), instructions);
            Assert.Contains("Esc / Ctrl+W", instructions);
            Assert.Contains(Strings.HistoryWindowCloseShortcutDescription, instructions);
            if (OperatingSystem.IsMacOS())
                Assert.Contains("Cmd+W", instructions);
        }
        finally
        {
            Strings.Culture = previousCulture;
        }
    }
}
