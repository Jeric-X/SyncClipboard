using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using System;

namespace SyncClipboard.Desktop.Controls;

public sealed partial class HistoryTagFlyout : Flyout
{
    public HistoryTagFlyout() => AvaloniaXamlLoader.Load(this);

    protected override void OnOpened()
    {
        if (Target is { } trigger && TopLevel.GetTopLevel(trigger) is { } owner)
        {
            // Track the trigger in window coordinates even when only an ancestor moves.
            Popup.PlacementTarget = owner;
            UpdateAnchor(trigger, EventArgs.Empty);
            trigger.LayoutUpdated += UpdateAnchor;
            trigger.EffectiveViewportChanged += OnViewportChanged;
        }
        base.OnOpened();
    }

    private void UpdateAnchor(object? sender, EventArgs _)
    {
        if (sender is Control trigger && Popup.PlacementTarget is TopLevel owner
            && trigger.TranslatePoint(default, owner) is { } position)
            Popup.PlacementRect = new Rect(position, trigger.Bounds.Size);
    }

    private void OnViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        if (sender is Control trigger && !e.EffectiveViewport.Intersects(new Rect(trigger.Bounds.Size)))
            Hide();
    }

    protected override void OnClosed()
    {
        if (Target is { } trigger)
        {
            trigger.LayoutUpdated -= UpdateAnchor;
            trigger.EffectiveViewportChanged -= OnViewportChanged;
        }
        base.OnClosed();
    }
}
