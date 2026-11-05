using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using System;

namespace SyncClipboard.Desktop.Controls;

public sealed partial class HistoryTagFlyout : Flyout
{
    private TopLevel? _owner;

    public HistoryTagFlyout() => AvaloniaXamlLoader.Load(this);

    protected override void OnOpened()
    {
        if (Target is { } trigger && TopLevel.GetTopLevel(trigger) is { } owner)
        {
            // Track the trigger in window coordinates even when only an ancestor moves.
            _owner = owner;
            Popup.PlacementTarget = owner;
            UpdateAnchor(trigger, EventArgs.Empty);
            trigger.LayoutUpdated += UpdateAnchor;
            trigger.EffectiveViewportChanged += OnViewportChanged;
            owner.AddHandler(InputElement.KeyDownEvent, OnOwnerKeyDown, RoutingStrategies.Tunnel);
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

    private void OnOwnerKeyDown(object? _, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Hide();
            return;
        }
        if (e.KeyModifiers != KeyModifiers.None || Content is not Control content ||
            content.FindAncestorOfType<ScrollViewer>() is not { } scroll)
            return;

        switch (e.Key)
        {
            case Key.Down: scroll.LineDown(); break;
            case Key.Up: scroll.LineUp(); break;
            case Key.PageDown: scroll.PageDown(); break;
            case Key.PageUp: scroll.PageUp(); break;
            default: return;
        }
        e.Handled = true;
    }

    protected override void OnClosed()
    {
        if (Target is { } trigger)
        {
            trigger.LayoutUpdated -= UpdateAnchor;
            trigger.EffectiveViewportChanged -= OnViewportChanged;
        }
        _owner?.RemoveHandler(InputElement.KeyDownEvent, OnOwnerKeyDown);
        _owner = null;
        base.OnClosed();
    }
}
