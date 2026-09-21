using Avalonia.Automation.Peers;
using SyncClipboard.Desktop.Controls;

namespace SyncClipboard.Test.Desktop;

[TestClass]
public class BreadcrumbAccessibilityTests
{
    [TestMethod]
    public void EmptyBreadcrumb_CanBeQueriedByAccessibility()
    {
        var repeater = new BreadcrumbItemsRepeater();
        var peer = ControlAutomationPeer.CreatePeerForElement(repeater);

        Assert.IsNotNull(peer);
        Assert.IsEmpty(peer.GetChildren());
    }
}
