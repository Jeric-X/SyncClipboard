using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.I18n;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Foundation;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class HistoryTagBarSettingsDialog : ContentDialog
{
    private static readonly TimeSpan RepositionDuration = TimeSpan.FromSeconds(0.5);
    private sealed record TagMotion(Point Position, TranslateTransform Transform, Storyboard Animation);
    private readonly Dictionary<HistoryTagBarItem, TagMotion> tagMotions = [];
    private readonly Func<Task> save;
    private object? settingsContent;
    private bool confirmingDiscard;
    private bool discardConfirmed;
    private Point pressPosition;
    private Point dragPosition;
    private Point previewOffset;
    private HistoryTagBarItem? pressedItem;
    private Border? pressedPill;
    private Pointer? pointer;
    private ToggleButton? pressedToggle;
    private ContentPresenter? dragPreview;
    private HistoryTagBarDragPreview? dragState;
    private double scrollMinHeight;
    private ScrollViewer? contentViewport;
    private readonly DispatcherTimer dragScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private int scrollDirection;
    public HistoryTagBarSettingsViewModel Model { get; }

    public HistoryTagBarSettingsDialog(HistoryTagBarSettingsViewModel model, Func<Task> save)
    {
        Model = model;
        this.save = save;
        InitializeComponent();
        DataContext = model;
        dragScrollTimer.Tick += (_, _) => ScrollDragStep();
        Closed += (_, _) =>
        {
            FinishDrag();
            foreach (var motion in tagMotions.Values)
                motion.Animation.Stop();
            tagMotions.Clear();
        };
        LayoutUpdated += (_, _) => AnimateTagPositions();
        Closing += OnClosing;
        AddHandler(PointerPressedEvent, new PointerEventHandler(OnPillPressed), true);
        AddHandler(PointerMovedEvent, new PointerEventHandler(OnPillMoved), true);
        AddHandler(PointerReleasedEvent, new PointerEventHandler(OnPillReleased), true);
        PointerCaptureLost += OnPillCaptureLost;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        contentViewport = GetTemplateChild("ContentScrollViewer") as ScrollViewer;
        if (GetTemplateChild("BackgroundElement") is FrameworkElement background)
        {
            background.MaxWidth = double.PositiveInfinity;
            background.HorizontalAlignment = HorizontalAlignment.Stretch;
            background.VerticalAlignment = VerticalAlignment.Stretch;
            background.MaxHeight = double.PositiveInfinity;
            background.Margin = new Thickness(16, 40, 16, 40);
        }
    }

    private async void OnPrimaryButtonClick(ContentDialog _, ContentDialogButtonClickEventArgs args)
    {
        if (confirmingDiscard)
        {
            discardConfirmed = true;
            args.Cancel = true;
            DispatcherQueue.TryEnqueue(Hide);
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

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
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

    private void OnPillPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || FindPressedPill(e.OriginalSource as DependencyObject, out var toggle) is not { DataContext: HistoryTagBarItem item } pill)
            return;
        FinishDrag();
        pressedItem = item;
        pressedPill = pill;
        pressPosition = e.GetCurrentPoint(this).Position;
        previewOffset = e.GetCurrentPoint(pill).Position;
        pointer = e.Pointer;
        pressedToggle = toggle;
        if (toggle is null && !CapturePointer(pointer))
            FinishDrag();
    }

    private static Border? FindPressedPill(DependencyObject? source, out ToggleButton? toggle)
    {
        toggle = null;
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is ToggleButton button)
                toggle = button;
            if (current is Border { Name: "TagPill" } pill)
                return pill;
        }
        return null;
    }

    private void OnPillMoved(object sender, PointerRoutedEventArgs e)
    {
        if (pressedItem is null || pressedPill is null)
            return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            FinishDrag();
            return;
        }
        if (dragPreview is null)
        {
            var position = e.GetCurrentPoint(this).Position;
            if (Math.Abs(position.X - pressPosition.X) < 4 && Math.Abs(position.Y - pressPosition.Y) < 4)
                return;
            if (!StartDragPreview())
                return;
        }
        e.Handled = true;
        dragPosition = e.GetCurrentPoint(this).Position;
        UpdateDragPreviewPosition();
        ScrollDuringDrag(GetDragPosition(_TagScroll));
        UpdateDropTarget();
    }

    private void UpdateDragPreviewPosition()
    {
        if (dragPreview is null)
            return;
        var point = GetDragPosition(_DragLayer);
        Canvas.SetLeft(dragPreview, point.X - previewOffset.X);
        Canvas.SetTop(dragPreview, point.Y - previewOffset.Y);
    }

    private bool StartDragPreview()
    {
        pressedToggle?.ReleasePointerCapture(pointer!);
        if (!CapturePointer(pointer!))
        {
            FinishDrag();
            return false;
        }
        dragPreview = new ContentPresenter
        {
            Content = pressedItem,
            ContentTemplate = _PinnedTags.ItemTemplate,
            Width = pressedPill!.ActualWidth,
            Height = pressedPill.ActualHeight,
            IsHitTestVisible = false,
            Opacity = 0.95
        };
        _DragLayer.Children.Add(dragPreview);
        scrollMinHeight = _TagScroll.MinHeight;
        _TagScroll.MinHeight = _TagScroll.ActualHeight;
        tagMotions.Remove(pressedItem!);
        dragState = new(Model, pressedItem!);
        _PinnedTags.ItemsSource = dragState.PinnedTags;
        RefreshDragLayout();
        return true;
    }

    private void UpdateDropTarget()
    {
        if (dragState is null)
            return;
        var position = GetDragPosition(_TagDropArea);
        if (contentViewport is null || !ContainsPointer(contentViewport)
            || position.X < 0 || position.X > _TagDropArea.ActualWidth || position.Y < 0)
            dragState.ClearTarget();
        else if (GetDragPosition(_PinDivider).Y < 0)
            PreviewPinnedPosition(GetDragPosition(_PinnedTags));
        else
            dragState.PreviewUnpinned();
        RefreshDragLayout();
    }

    private Point GetDragPosition(UIElement element) => TransformToVisual(element).TransformPoint(dragPosition);

    private bool ContainsPointer(FrameworkElement element) =>
        new Rect(0, 0, element.ActualWidth, element.ActualHeight).Contains(GetDragPosition(element));

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
        var container = (FrameworkElement)_PinnedTags.ContainerFromIndex(index);
        var origin = container.TransformToVisual(_PinnedTags).TransformPoint(default);
        var after = position.X - origin.X > container.ActualWidth / 2;
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
            if (_PinnedTags.ContainerFromIndex(index) is not FrameworkElement container)
                continue;
            var bounds = container.TransformToVisual(_PinnedTags)
                .TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
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
        for (var index = 0; index < dragState!.PinnedTags.Count; index++)
        {
            if (_PinnedTags.ContainerFromIndex(index) is UIElement container)
                container.Opacity = dragState.PinnedTags[index] == pressedItem ? 0 : 1;
        }
        var sourceIndex = Model.UnpinnedTags.IndexOf(pressedItem!);
        if (sourceIndex >= 0 && _UnpinnedTags.ContainerFromIndex(sourceIndex) is UIElement source)
            source.Opacity = 0.25;
    }

    private void OnPillReleased(object _, PointerRoutedEventArgs e)
    {
        if (dragPreview is not null)
        {
            dragPosition = e.GetCurrentPoint(this).Position;
            UpdateDropTarget();
        }
        var preview = dragState;
        FinishDrag();
        preview?.Commit();
    }

    private void OnPillCaptureLost(object _, PointerRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, this))
            FinishDrag();
    }

    private void FinishDrag()
    {
        dragScrollTimer.Stop();
        scrollDirection = 0;
        var captured = pointer;
        pointer = null;
        if (dragState is not null)
        {
            RestoreOpacity(_PinnedTags);
            RestoreOpacity(_UnpinnedTags);
            _PinnedTags.ItemsSource = Model.PinnedTags;
            dragState = null;
            _TagScroll.MinHeight = scrollMinHeight;
        }
        _DragLayer.Children.Clear();
        dragPreview = null;
        pressedPill = null;
        pressedItem = null;
        pressedToggle = null;
        if (captured is not null)
            ReleasePointerCapture(captured);
    }

    private static void RestoreOpacity(ItemsControl items)
    {
        for (var index = 0; index < items.Items.Count; index++)
        {
            if (items.ContainerFromIndex(index) is UIElement container)
                container.Opacity = 1;
        }
    }

    private void AnimateTagPositions()
    {
        AnimateTagPositions(_PinnedTags);
        AnimateTagPositions(_UnpinnedTags);
    }

    private void AnimateTagPositions(ItemsControl items)
    {
        for (var index = 0; index < items.Items.Count; index++)
        {
            if (items.Items[index] is not HistoryTagBarItem item || (dragState is not null && item == pressedItem)
                || items.ContainerFromIndex(index) is not FrameworkElement container)
                continue;
            var pill = FindTagPill(container);
            if (pill is not null)
                AnimateTagPosition(item, pill, container.TransformToVisual((UIElement)_TagScroll.Content).TransformPoint(default));
        }
    }

    private static Border? FindTagPill(DependencyObject root)
    {
        if (root is Border { Name: "TagPill" } pill)
            return pill;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            if (FindTagPill(VisualTreeHelper.GetChild(root, index)) is { } found)
                return found;
        }
        return null;
    }

    private void AnimateTagPosition(HistoryTagBarItem item, Border pill, Point position)
    {
        if (!tagMotions.TryGetValue(item, out var previous))
        {
            var initial = new TranslateTransform();
            pill.RenderTransform = initial;
            tagMotions[item] = new(position, initial, new Storyboard());
            return;
        }
        if (previous.Position == position)
        {
            pill.RenderTransform = previous.Transform;
            return;
        }
        var dx = previous.Position.X + previous.Transform.X - position.X;
        var dy = previous.Position.Y + previous.Transform.Y - position.Y;
        previous.Animation.Stop();
        var transform = new TranslateTransform { X = dx, Y = dy };
        pill.RenderTransform = transform;
        var animation = new Storyboard();
        AddMovement(animation, transform, nameof(TranslateTransform.X), dx);
        AddMovement(animation, transform, nameof(TranslateTransform.Y), dy);
        tagMotions[item] = new(position, transform, animation);
        animation.Begin();
    }

    private static void AddMovement(Storyboard storyboard, TranslateTransform transform, string property, double offset)
    {
        var animation = new DoubleAnimation
        {
            From = offset,
            To = 0,
            Duration = new Duration(RepositionDuration),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(animation, transform);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }

    private void ScrollDuringDrag(Point position)
    {
        if (contentViewport is null)
            return;
        var origin = contentViewport.TransformToVisual(_TagScroll).TransformPoint(default);
        var top = Math.Max(0, origin.Y);
        var bottom = Math.Min(_TagScroll.ActualHeight, origin.Y + contentViewport.ActualHeight);
        scrollDirection = GetScrollDirection(position, _TagScroll.ActualWidth, top, bottom);
        if (scrollDirection == 0)
            dragScrollTimer.Stop();
        else if (!dragScrollTimer.IsEnabled)
            dragScrollTimer.Start();
    }

    private void ScrollDragStep()
    {
        if (dragState is null || contentViewport is null)
            return;
        var offset = Math.Clamp(_TagScroll.VerticalOffset + (scrollDirection * 12), 0, _TagScroll.ScrollableHeight);
        var remaining = (scrollDirection * 12) - (offset - _TagScroll.VerticalOffset);
        _TagScroll.ChangeView(null, offset, null, true);
        contentViewport.ChangeView(null, contentViewport.VerticalOffset + remaining, null, true);
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateLayout();
            UpdateDropTarget();
            UpdateDragPreviewPosition();
        });
    }

    private static int GetScrollDirection(Point position, double width, double top, double bottom)
    {
        if (position.X < 0 || position.X > width || position.Y < top || position.Y > bottom)
            return 0;
        if (position.Y < top + 24)
            return -1;
        return position.Y > bottom - 24 ? 1 : 0;
    }
}
