using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SyncClipboard.Core.I18n;
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

    private readonly Button _more = new()
    {
        // Reserve the same width for +1 through +32 so the count cannot affect which tags fit.
        Focusable = false, Width = 28, Height = 22, MinWidth = 0, MinHeight = 0, FontSize = 11,
        Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(0), CornerRadius = new CornerRadius(5)
    };
    private HistoryPreviewTag[] _tags = [];
    private HistoryTagSize[] _sizes = [];
    private int _visibleCount = -1;
    private WrapPanel? _expanded;
    private Flyout? _flyout;
    private IDisposable? _hold;
    private HistoryViewModel? _popupViewModel;

    public HistoryInlineTagsView()
    {
        ClipToBounds = true;
        _more.Classes.Add("HistoryTagOverflow");
        Children.Add(_more);
        ToolTip.SetTip(_more, Strings.HistoryTagsShowMore);
        AutomationProperties.SetName(_more, Strings.HistoryTagsShowMore);
        _more.Click += ShowMore;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DataContextProperty || change.Property == ViewModelProperty ||
            (change.Property == IsVisibleProperty && !IsVisible))
            _flyout?.Hide();
        if (change.Property == TagsProperty || change.Property == IsVisibleProperty)
            UpdateTags();
    }

    private void UpdateTags()
    {
        var tags = IsVisible ? Tags?.ToArray() ?? [] : [];
        if (_tags.SequenceEqual(tags))
            return;

        _tags = tags;
        _visibleCount = -1;
        Children.RemoveRange(0, Children.Count - 1);
        foreach (var tag in tags)
        {
            var chip = CreateChip(tag);
            chip.Margin = new Thickness(4, 0, 0, 0);
            Children.Insert(Children.Count - 1, chip);
        }
        if (tags.Length == 0)
            _flyout?.Hide();
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
        // DockPanel measures this column before the record. Leave at least half the content width for the text or image.
        var width = Math.Min(200, availableSize.Width / 2);
        foreach (var child in Children)
            child.Measure(new Size(width, double.PositiveInfinity));
        _sizes = Children.Take(_tags.Length).Select(c => new HistoryTagSize(c.DesiredSize.Width, c.DesiredSize.Height)).ToArray();
        // Only the record content and its controls determine row height.
        return new Size(Math.Min(width, _sizes.Sum(size => size.Width)), 0);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var more = new HistoryTagSize(_more.DesiredSize.Width, _more.DesiredSize.Height);
        var layout = HistoryTagOverflowLayout.Calculate(_sizes, more, finalSize.Width, finalSize.Height, lineSpacing: 4);
        var hiddenCount = _tags.Length - layout.Tags.Length;
        _more.Content = layout.More.HasValue ? $"+{hiddenCount}" : null;
        var top = Math.Max(0, (finalSize.Height - layout.Height) / 2);
        // Keep overflow items measured for resizing; their slots lie entirely outside the clipped panel.
        for (var i = 0; i < Children.Count; i++)
        {
            HistoryTagPlacement? slot = i == _tags.Length ? layout.More : i < layout.Tags.Length ? layout.Tags[i] : null;
            var child = Children[i];
            var rect = slot is { } p ? new Rect(p.X, top + p.Y, p.Width, p.Height)
                : new Rect(finalSize.Width + 1, 0, child.DesiredSize.Width, child.DesiredSize.Height);
            child.Arrange(rect);
            SetSlotAccessibility(child, slot.HasValue);
        }
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
            expanded.Children.Add(CreateChip(_tags[i]));
    }

    private void ShowMore(object? _, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_flyout is not null || _visibleCount < 0 || _visibleCount >= _tags.Length || ViewModel is not { } vm)
            return;
        _expanded = new WrapPanel { MaxWidth = 224, ItemSpacing = 5, LineSpacing = 5 };
        FillPopup(_expanded);
        var flyout = new HistoryTagFlyout
        {
            VerticalOffset = 4,
            Content = _expanded
        };
        _flyout = flyout;
        FlyoutBase.SetAttachedFlyout(_more, flyout);
        var owner = TopLevel.GetTopLevel(this) as Window;
        flyout.Closed += (_, _) => ReleasePopup(owner);
        _popupViewModel = vm;
        vm.PropertyChanged += OnPopupViewModelChanged;
        _hold = vm.HoldForModalOperation();
        try
        {
            FlyoutBase.ShowAttachedFlyout(_more);
            if (!flyout.IsOpen)
                ReleasePopup(owner);
        }
        catch
        {
            ReleasePopup(owner);
            throw;
        }
    }

    private void OnPopupViewModelChanged(object? _, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HistoryViewModel.SelectedItem))
            _flyout?.Hide();
    }

    private void ReleasePopup(Window? owner)
    {
        if (_popupViewModel is not { } vm)
            return;
        vm.PropertyChanged -= OnPopupViewModelChanged;
        _popupViewModel = null;
        _hold?.Dispose();
        _hold = null;
        _expanded = null;
        _flyout = null;
        FlyoutBase.SetAttachedFlyout(_more, null);

        // Let focus return from a native popup before checking whether the user left history.
        Dispatcher.UIThread.Post(() =>
        {
            if (owner is { IsVisible: true, IsActive: false })
                vm.OnLostFocus();
        }, DispatcherPriority.Background);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _flyout?.Hide();
        base.OnDetachedFromVisualTree(e);
    }
}
