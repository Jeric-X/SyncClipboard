using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.I18n;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.Views;

public partial class HistoryTagBarSettingsDialog : FAContentDialog
{
    private static readonly TimeSpan RepositionDuration = TimeSpan.FromSeconds(0.5);
    private sealed record TagMotion(Point Position, TranslateTransform Transform);
    private readonly Dictionary<HistoryTagBarItem, TagMotion> tagMotions = [];
    private readonly Func<Task> save;
    private object? settingsContent;
    private bool confirmingDiscard;
    private bool discardConfirmed;
    private Point pressPosition;
    private Point previewOffset;
    private HistoryTagBarItem? pressedItem;
    private Border? pressedPill;
    private IPointer? pointer;
    private ContentPresenter? dragPreview;
    private OverlayLayer? dragLayer;
    private HistoryTagBarDragPreview? dragState;
    private double scrollMinHeight;
    public HistoryTagBarSettingsViewModel Model { get; }
    protected override Type StyleKeyOverride => typeof(FAContentDialog);

    public HistoryTagBarSettingsDialog() : this(new(new Dictionary<string, int>(), []), () => Task.CompletedTask) { }

    public HistoryTagBarSettingsDialog(HistoryTagBarSettingsViewModel model, Func<Task> save)
    {
        Model = model;
        this.save = save;
        InitializeComponent();
        DataContext = model;
        Closed += (_, _) =>
        {
            FinishDrag();
            foreach (var motion in tagMotions.Values)
                motion.Transform.Transitions = null;
            tagMotions.Clear();
        };
        LayoutUpdated += (_, _) => AnimateTagPositions();
        Closing += OnClosing;
        AddHandler(PointerPressedEvent, OnPillPressed, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnPillMoved, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPillReleased, handledEventsToo: true);
        PointerCaptureLost += OnPillCaptureLost;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<Border>("BackgroundElement") is { } background)
        {
            background.HorizontalAlignment = HorizontalAlignment.Stretch;
            background.VerticalAlignment = VerticalAlignment.Stretch;
            background.MaxHeight = double.PositiveInfinity;
            background.Margin = new Thickness(16, 40);
        }
    }

    private async void OnPrimaryButtonClick(FAContentDialog _, FAContentDialogButtonClickEventArgs args)
    {
        if (confirmingDiscard)
        {
            discardConfirmed = true;
            args.Cancel = true;
            Dispatcher.UIThread.Post(Hide);
            return;
        }
        var deferral = args.GetDeferral();
        try
        {
            await save();
            Model.AcceptChanges();
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            Model.Error = ex.Message;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnClosing(FAContentDialog sender, FAContentDialogClosingEventArgs args)
    {
        if (discardConfirmed || !Model.HasChanges)
            return;
        args.Cancel = true;
        FinishDrag();
        if (confirmingDiscard)
        {
            Content = settingsContent;
            Title = Strings.HistoryTagBarSettings;
            PrimaryButtonText = Strings.HistoryTagsApply;
            CloseButtonText = Strings.Cancel;
            confirmingDiscard = false;
            return;
        }
        settingsContent = Content;
        confirmingDiscard = true;
        Title = Strings.HistoryTagBarDiscardTitle;
        Content = new TextBlock { Text = Strings.HistoryTagBarDiscardMessage, TextWrapping = TextWrapping.Wrap };
        PrimaryButtonText = Strings.HistoryTagBarDiscard;
        CloseButtonText = Strings.HistoryTagBarContinueEditing;
    }

    private void OnPillPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Visual source
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        var ancestors = source.GetVisualAncestors().Prepend(source).ToArray();
        var pill = ancestors.OfType<Border>().FirstOrDefault(b => b.Name == "TagPill");
        if (pill?.DataContext is not HistoryTagBarItem item)
            return;
        FinishDrag();
        pressedItem = item;
        pressedPill = pill;
        pressPosition = e.GetPosition(this);
        previewOffset = e.GetPosition(pill);
        pointer = e.Pointer;
        if (!ancestors.OfType<ToggleButton>().Any())
            pointer.Capture(this);
    }

    private void OnPillMoved(object? _, PointerEventArgs e)
    {
        if (pressedItem is null || pressedPill is null)
            return;
        if (dragPreview is null)
        {
            var distance = e.GetPosition(this) - pressPosition;
            if (Math.Abs(distance.X) < 4 && Math.Abs(distance.Y) < 4)
                return;
            if (!StartDragPreview())
                return;
        }
        e.Handled = true;
        var position = e.GetPosition(dragLayer!);
        Canvas.SetLeft(dragPreview!, position.X - previewOffset.X);
        Canvas.SetTop(dragPreview!, position.Y - previewOffset.Y);
        ScrollDuringDrag(e.GetPosition(_TagScroll));
        UpdateDropTarget(e);
    }

    private bool StartDragPreview()
    {
        dragLayer = OverlayLayer.GetOverlayLayer(this);
        if (dragLayer is null || pressedPill is null)
            return false;
        pointer?.Capture(this);
        // Reuse the actual pill template in logical units, without a bitmap or OS drag image.
        dragPreview = new ContentPresenter
        {
            Name = "TagDragPreview",
            Content = pressedItem,
            ContentTemplate = _PinnedTags.ItemTemplate,
            Width = pressedPill.Bounds.Width,
            Height = pressedPill.Bounds.Height,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Opacity = 0.95,
            ZIndex = int.MaxValue
        };
        dragLayer.Children.Add(dragPreview);
        scrollMinHeight = _TagScroll.MinHeight;
        _TagScroll.MinHeight = _TagScroll.Bounds.Height;
        tagMotions.Remove(pressedItem!);
        dragState = new(Model, pressedItem!);
        _PinnedTags.SetCurrentValue(ItemsControl.ItemsSourceProperty, dragState.PinnedTags);
        RefreshDragLayout();
        return true;
    }

    private void UpdateDropTarget(PointerEventArgs e)
    {
        if (dragState is null)
            return;
        if (!ContainsPointer(_TagScroll, e))
            dragState.ClearTarget();
        else if (ContainsPointer(_PinnedArea, e))
            PreviewPinnedPosition(e.GetPosition(_PinnedTags));
        else if (ContainsPointer(_UnpinnedArea, e))
            dragState.PreviewUnpinned();
        else
            dragState.ClearTarget();
        RefreshDragLayout();
    }

    private static bool ContainsPointer(Control control, PointerEventArgs e) =>
        new Rect(control.Bounds.Size).Contains(e.GetPosition(control));

    private void PreviewPinnedPosition(Point position)
    {
        var index = FindDropTarget(position);
        if (index < 0)
        {
            dragState!.PreviewPinned(0);
            return;
        }
        if (dragState!.PinnedTags[index] == pressedItem)
            return;
        var container = _PinnedTags.ContainerFromIndex(index)!;
        var origin = container.TranslatePoint(default, _PinnedTags)!.Value;
        var after = position.X - origin.X > container.Bounds.Width / 2;
        var sourceIndex = dragState.PinnedTags.IndexOf(pressedItem!);
        var boundary = index + (after ? 1 : 0);
        if (sourceIndex >= 0 && sourceIndex < boundary)
            boundary--;
        dragState.PreviewPinned(boundary);
    }

    private int FindDropTarget(Point position)
    {
        var nearest = -1;
        var nearestDistance = double.PositiveInfinity;
        for (var index = 0; index < dragState!.PinnedTags.Count; index++)
        {
            if (_PinnedTags.ContainerFromIndex(index) is not Control container
                || container.TranslatePoint(default, _PinnedTags) is not { } origin)
                continue;
            var bounds = new Rect(origin, container.Bounds.Size);
            var dx = Math.Max(bounds.Left - position.X, Math.Max(0, position.X - bounds.Right));
            var dy = Math.Max(bounds.Top - position.Y, Math.Max(0, position.Y - bounds.Bottom));
            var distance = (dx * dx) + (dy * dy);
            if (distance < nearestDistance)
            {
                nearest = index;
                nearestDistance = distance;
            }
        }
        return nearest;
    }

    private void RefreshDragLayout()
    {
        _PinnedTags.UpdateLayout();
        foreach (var container in _PinnedTags.GetRealizedContainers())
            container.Opacity = container.DataContext == pressedItem ? 0 : 1;
        foreach (var container in _UnpinnedTags.GetRealizedContainers())
            container.Opacity = container.DataContext == pressedItem ? 0.25 : 1;
    }

    private void OnPillReleased(object? _, PointerReleasedEventArgs e)
    {
        if (dragPreview is not null)
            UpdateDropTarget(e);
        var preview = dragState;
        FinishDrag();
        preview?.Commit();
    }

    private void OnPillCaptureLost(object? _, PointerCaptureLostEventArgs e)
    {
        if (e.Source == this)
            FinishDrag();
    }

    private void FinishDrag()
    {
        var capturedPointer = pointer;
        pointer = null;
        if (dragState is not null)
        {
            foreach (var container in _PinnedTags.GetRealizedContainers().Concat(_UnpinnedTags.GetRealizedContainers()))
                container.Opacity = 1;
            _PinnedTags.SetCurrentValue(ItemsControl.ItemsSourceProperty, Model.PinnedTags);
            dragState = null;
            _TagScroll.MinHeight = scrollMinHeight;
        }
        if (dragPreview is not null)
            dragLayer?.Children.Remove(dragPreview);
        dragPreview = null;
        dragLayer = null;
        pressedPill = null;
        pressedItem = null;
        capturedPointer?.Capture(null);
    }

    private void AnimateTagPositions()
    {
        foreach (var container in _PinnedTags.GetRealizedContainers().Concat(_UnpinnedTags.GetRealizedContainers()))
        {
            if (container.DataContext is not HistoryTagBarItem item || item == pressedItem
                || container.TranslatePoint(default, (Visual)_TagScroll.Content!) is not { } position)
                continue;
            var pill = container.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "TagPill");
            if (pill is not null)
                AnimateTagPosition(item, pill, position);
        }
    }

    private void AnimateTagPosition(HistoryTagBarItem item, Border pill, Point position)
    {
        if (!tagMotions.TryGetValue(item, out var previous))
        {
            var initial = new TranslateTransform();
            pill.RenderTransform = initial;
            tagMotions[item] = new(position, initial);
            return;
        }
        if (previous.Position == position)
        {
            pill.RenderTransform = previous.Transform;
            return;
        }
        // Start from the currently displayed point when another layout interrupts the movement.
        var offset = previous.Position - position + new Vector(previous.Transform.X, previous.Transform.Y);
        previous.Transform.Transitions = null;
        var transform = new TranslateTransform(offset.X, offset.Y);
        pill.RenderTransform = transform;
        tagMotions[item] = new(position, transform);
        transform.Transitions =
        [
            new DoubleTransition { Property = TranslateTransform.XProperty, Duration = RepositionDuration, Easing = new CubicEaseOut() },
            new DoubleTransition { Property = TranslateTransform.YProperty, Duration = RepositionDuration, Easing = new CubicEaseOut() }
        ];
        transform.X = 0;
        transform.Y = 0;
    }

    private void ScrollDuringDrag(Point position)
    {
        if (position.Y < 24)
            _TagScroll.Offset -= new Vector(0, 12);
        else if (position.Y > _TagScroll.Bounds.Height - 24)
            _TagScroll.Offset += new Vector(0, 12);
    }
}
