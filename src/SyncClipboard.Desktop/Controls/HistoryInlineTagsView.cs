using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.ViewModels.Sub;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace SyncClipboard.Desktop.Controls;

public sealed class HistoryInlineTagsView : Panel
{
    public static readonly StyledProperty<IEnumerable<HistoryPreviewTag>?> TagsProperty =
        AvaloniaProperty.Register<HistoryInlineTagsView, IEnumerable<HistoryPreviewTag>?>(nameof(Tags));

    public static readonly StyledProperty<HistoryViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<HistoryInlineTagsView, HistoryViewModel?>(nameof(ViewModel));

    public static readonly StyledProperty<IEnumerable<string>?> PinnedTagsProperty =
        AvaloniaProperty.Register<HistoryInlineTagsView, IEnumerable<string>?>(nameof(PinnedTags));

    public IEnumerable<string>? PinnedTags
    {
        get => GetValue(PinnedTagsProperty);
        set => SetValue(PinnedTagsProperty, value);
    }

    public IEnumerable<HistoryPreviewTag>? Tags
    {
        get => GetValue(TagsProperty);
        set => SetValue(TagsProperty, value);
    }

    public HistoryViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private Button? _more;
    private readonly List<HistoryRecordTagChip> _widthProbes = [];
    private HistoryPreviewTag[] _tags = [];
    private int _measuredForWidth;
    private int _measuredCount;
    private int _visibleCount = -1;
    private WrapPanel? _expanded;
    private Flyout? _flyout;
    private HistoryViewModel? _popupViewModel;

    public HistoryInlineTagsView() => ClipToBounds = true;

    private Button CreateMoreButton()
    {
        var button = new Button
        {
            // Reserve the same width for +1 through +32 so the count cannot affect which tags fit.
            Focusable = false,
            Width = 28,
            Height = 22,
            MinWidth = 0,
            MinHeight = 0,
            FontSize = 11,
            Margin = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(5)
        };
        button.Classes.Add("HistoryTagOverflow");
        ToolTip.SetTip(button, Strings.HistoryTagsShowMore);
        AutomationProperties.SetName(button, Strings.HistoryTagsShowMore);
        button.Click += ShowMore;
        return button;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DataContextProperty || change.Property == ViewModelProperty ||
            (change.Property == IsVisibleProperty && !IsVisible))
            _flyout?.Hide();
        if (change.Property == TagsProperty || change.Property == PinnedTagsProperty || change.Property == IsVisibleProperty)
            UpdateTags();
    }

    private void UpdateTags()
    {
        var tags = IsVisible ? HistoryTagOrdering.OrderForDisplay(Tags, PinnedTags) : [];
        if (_tags.SequenceEqual(tags))
            return;

        foreach (var probe in _widthProbes)
            LogicalChildren.Remove(probe);
        _widthProbes.Clear();
        _tags = tags;
        _visibleCount = -1;
        if (tags.Length == 0)
        {
            _flyout?.Hide();
            Children.Clear();
            _more = null;
        }
        else if (_more is null)
        {
            _more = CreateMoreButton();
            Children.Add(_more);
        }
        else
        {
            Children.RemoveRange(0, Children.Count - 1);
        }
        InvalidateMeasure();
    }

    private HistoryRecordTagChip CreateChip(HistoryPreviewTag tag)
    {
        var chip = new HistoryRecordTagChip { DataContext = tag };
        chip.RemoveRequested += RemoveTag;
        return chip;
    }

    private async void RemoveTag(object? _, HistoryPreviewTag tag)
    {
        if (DataContext is HistoryRecordVM record && record.Key == tag.Record && ViewModel is { } vm)
            await vm.RemoveRecordTagAsync(tag);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_more is null)
            return default;

        // DockPanel measures this column before the record. Leave at least half the content width for the text or image.
        var width = Math.Min(200, availableSize.Width / 2);
        _more.Measure(new Size(width, double.PositiveInfinity));
        _measuredForWidth = 0;
        var desiredWidth = 0d;
        foreach (var size in MeasureTags(width))
        {
            _measuredForWidth++;
            desiredWidth += size.Width;
            if (desiredWidth >= width)
                break;
        }
        // Only the record content and its controls determine row height.
        return new Size(Math.Min(width, desiredWidth), 0);
    }

    private IEnumerable<HistoryTagSize> MeasureTags(double width)
    {
        _measuredCount = 0;
        for (var i = 0; i < _tags.Length; i++)
        {
            if (i == Children.Count - 1)
            {
                var chip = CreateChip(_tags[i]);
                chip.Margin = new Thickness(4, 0, 0, 0);
                Children.Insert(i, chip);
            }
            if (i == _widthProbes.Count)
            {
                // Keep a stable binding per tag: rebinding during layout can reuse the previous label's measurement.
                var probe = new HistoryRecordTagChip { DataContext = _tags[i], Margin = new Thickness(4, 0, 0, 0) };
                LogicalChildren.Add(probe);
                _widthProbes.Add(probe);
            }
            var widthProbe = _widthProbes[i];
            widthProbe.Measure(new Size(width, double.PositiveInfinity));
            _measuredCount++;
            yield return new(widthProbe.DesiredSize.Width, widthProbe.DesiredSize.Height);
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_more is null)
            return finalSize;

        var more = new HistoryTagSize(_more.DesiredSize.Width, _more.DesiredSize.Height);
        var layout = HistoryTagOverflowLayout.Calculate(MeasureTags(finalSize.Width), more,
            finalSize.Width, finalSize.Height, lineSpacing: 4);
        // Keep the measured prefix and the first item that did not fit; the rest stay as data.
        var keep = Math.Max(_measuredForWidth, _measuredCount);
        if (Children.Count - 1 > keep)
            Children.RemoveRange(keep, Children.Count - 1 - keep);
        var hiddenCount = _tags.Length - layout.Tags.Length;
        _more.Content = layout.More.HasValue ? $"+{hiddenCount}" : null;
        ArrangeChildren(layout, finalSize);
        if (_visibleCount != layout.Tags.Length)
        {
            _visibleCount = layout.Tags.Length;
            if (_expanded is { } expanded)
                FillPopup(expanded);
        }
        if (layout.More is null)
            _flyout?.Hide();
        return finalSize;
    }

    private void ArrangeChildren(HistoryTagOverflowLayout layout, Size finalSize)
    {
        var top = Math.Max(0, (finalSize.Height - layout.Height) / 2);
        // Keep overflow items measured for resizing; their slots lie entirely outside the clipped panel.
        for (var i = 0; i < Children.Count; i++)
        {
            var childIsMore = Children[i] == _more;
            HistoryTagPlacement? slot = childIsMore ? layout.More : i < layout.Tags.Length ? layout.Tags[i] : null;
            var child = Children[i];
            var constraint = slot is { } size ? new Size(size.Width, size.Height)
                : new Size(finalSize.Width, double.PositiveInfinity);
            child.Measure(constraint);
            var rect = slot is { } p ? new Rect(p.X, top + p.Y, p.Width, p.Height)
                : new Rect(finalSize.Width + 1, 0, child.DesiredSize.Width, child.DesiredSize.Height);
            child.Arrange(rect);
            SetSlotAccessibility(child, slot.HasValue);
        }
    }

    private static void SetSlotAccessibility(Control child, bool visible)
    {
        child.IsEnabled = visible;
        var view = visible ? AccessibilityView.Default : AccessibilityView.Raw;
        if (AutomationProperties.GetAccessibilityView(child) == view)
            return;

        var offscreen = visible ? IsOffscreenBehavior.Default : IsOffscreenBehavior.Offscreen;
        foreach (var element in child.GetSelfAndVisualDescendants().OfType<Control>())
        {
            AutomationProperties.SetAccessibilityView(element, view);
            AutomationProperties.SetIsOffscreenBehavior(element, offscreen);
        }
    }

    private void FillPopup(WrapPanel expanded)
    {
        expanded.Children.Clear();
        for (var i = _visibleCount; i < _tags.Length; i++)
        {
            expanded.Children.Add(CreateChip(_tags[i]));
        }
    }

    private void ShowMore(object? _, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_flyout is not null || !IsEffectivelyVisible || _more?.Content is null || _visibleCount < 0 || ViewModel is not { } vm)
            return;
        var bounds = _more.GetTransformedBounds();
        if (bounds is not { } b || !b.Bounds.TransformToAABB(b.Transform).Intersects(b.Clip))
            return;

        OpenTagFlyout(_more, vm);
    }

    private void OpenTagFlyout(Button trigger, HistoryViewModel vm)
    {
        _expanded = new WrapPanel { MaxWidth = 224, ItemSpacing = 5, LineSpacing = 5 };
        FillPopup(_expanded);
        var flyout = new HistoryTagFlyout
        {
            VerticalOffset = 4,
            Content = _expanded
        };
        _flyout = flyout;
        FlyoutBase.SetAttachedFlyout(trigger, flyout);
        flyout.Closed += (_, _) => ReleasePopup(trigger);
        _popupViewModel = vm;
        vm.PropertyChanged += OnPopupViewModelChanged;
        try
        {
            FlyoutBase.ShowAttachedFlyout(trigger);
            if (!flyout.IsOpen)
                ReleasePopup(trigger);
        }
        catch
        {
            ReleasePopup(trigger);
            throw;
        }
    }

    private void OnPopupViewModelChanged(object? _, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HistoryViewModel.SelectedItem))
            _flyout?.Hide();
    }

    private void ReleasePopup(Button trigger)
    {
        if (_popupViewModel is not { } vm)
            return;
        vm.PropertyChanged -= OnPopupViewModelChanged;
        _popupViewModel = null;
        _expanded = null;
        _flyout = null;
        FlyoutBase.SetAttachedFlyout(trigger, null);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _flyout?.Hide();
        base.OnDetachedFromVisualTree(e);
    }
}
