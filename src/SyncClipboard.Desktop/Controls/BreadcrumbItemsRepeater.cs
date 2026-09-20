using Avalonia.Automation.Peers;
using FluentAvalonia.UI.Controls;

namespace SyncClipboard.Desktop.Controls;

public class BreadcrumbItemsRepeater : FAItemsRepeater
{
    // FluentAvalonia 3.1 assumes an empty repeater always has a non-null children list.
    // Breadcrumbs are not virtualized, so Avalonia's visual-tree peer is sufficient here.
    protected override AutomationPeer OnCreateAutomationPeer() => new ControlAutomationPeer(this);
}
