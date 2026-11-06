using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.ViewModels.Sub;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Windows.Foundation;

namespace SyncClipboard.WinUI3.Controls;

public sealed class HistoryInlineTagsView : Panel
{
    public static readonly DependencyProperty TagsProperty = DependencyProperty.Register(
        nameof(Tags), typeof(object), typeof(HistoryInlineTagsView),
        new PropertyMetadata(null, (sender, _) => ((HistoryInlineTagsView)sender).UpdateTags()));

    public IEnumerable<HistoryPreviewTag>? Tags
    {
        get => (IEnumerable<HistoryPreviewTag>?)GetValue(TagsProperty);
        set => SetValue(TagsProperty, value);
    }

    public DataTemplate OverflowTemplate { get; set; } = null!;
    internal HistoryViewModel? ViewModel { get; set; }

    private HistoryPreviewTag[] _tags = [];
    private HistoryRecordTagChip? _widthProbe;
    private readonly List<HistoryRecordTagChip> _chips = [];
    private HistoryTagOverflowLayout _layout = new([], null, 0);
    private int _visibleCount = -1;
    private Button? _more;
    private Rect? _effectiveViewport;
    private Flyout? _flyout;
    private WrapPanel? _expanded;

    public HistoryInlineTagsView()
    {
        SizeChanged += (_, _) => InvalidateMeasure();
        Unloaded += (_, _) => _flyout?.Hide();
        DataContextChanged += (_, _) => _flyout?.Hide();
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => UpdateTags());
    }

    private void UpdateTags()
    {
        var tags = Visibility == Visibility.Visible ? Tags?.ToArray() ?? [] : [];
        if (_tags.SequenceEqual(tags))
            return;

        if (tags.Length == 0 || (_tags.Length > 0 && _tags[0].Record != tags[0].Record))
            _flyout?.Hide();
        foreach (var chip in _chips)
        {
            if (Children.Contains(chip))
                Children.Remove(chip);
        }
        _chips.Clear();
        _layout = new([], null, 0);
        _tags = tags;
        _visibleCount = -1;
        if (tags.Length == 0)
        {
            Children.Clear();
            _widthProbe = null;
            _more = null;
            _effectiveViewport = null;
        }
        InvalidateMeasure();
    }

    private HistoryRecordTagChip CreateChip(HistoryPreviewTag tag)
    {
        var chip = new HistoryRecordTagChip { DataContext = tag };
        chip.RemoveRequested += RemoveTag;
        return chip;
    }

    private async void RemoveTag(object? sender, HistoryPreviewTag tag)
    {
        if (DataContext is HistoryRecordVM record && record.Key == tag.Record && ViewModel is { } vm)
            await vm.RemoveRecordTagAsync(tag);
    }

    private IEnumerable<HistoryTagSize> MeasureTags()
    {
        _widthProbe ??= new HistoryRecordTagChip { Margin = new Thickness(4, 0, 0, 0) };
        foreach (var tag in _tags)
        {
            _widthProbe.DataContext = tag;
            // Keep natural widths independent of the column width used to arrange truncated chips.
            _widthProbe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = _widthProbe.DesiredSize;
            yield return new HistoryTagSize(size.Width, size.Height);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_tags.Length == 0)
            return default;

        if (_more is null)
        {
            _more = (Button)OverflowTemplate.LoadContent();
            _more.Click += OpenOverflow;
            _more.EffectiveViewportChanged += TriggerViewportChanged;
            _more.Unloaded += (sender, _) =>
            {
                if (ReferenceEquals(sender, _more))
                    _effectiveViewport = null;
            };
        }
        var width = Math.Min(200, availableSize.Width / 2);
        _more.Measure(new Size(width, double.PositiveInfinity));
        var desiredWidth = MeasureColumnWidth(width);
        if (ActualWidth > 0 && ActualHeight > 0)
        {
            var moreSize = new HistoryTagSize(_more.DesiredSize.Width, _more.DesiredSize.Height);
            _layout = HistoryTagOverflowLayout.Calculate(MeasureTags(), moreSize,
                ActualWidth, ActualHeight, lineSpacing: 4);
        }
        else
        {
            _layout = new([], null, 0);
        }
        SyncLayoutChildren(_more);
        // Let the record content and its actions determine the row height.
        return new Size(desiredWidth, 0);
    }

    private double MeasureColumnWidth(double limit)
    {
        var width = 0d;
        foreach (var size in MeasureTags())
        {
            width += size.Width;
            if (width >= limit)
                return limit;
        }
        return width;
    }

    private void SyncLayoutChildren(Button more)
    {
        SyncTagChips();
        if (_layout.More.HasValue && !Children.Contains(more))
            Children.Add(more);
        else if (!_layout.More.HasValue && Children.Contains(more))
            Children.Remove(more);
        if (!_layout.More.HasValue)
            _flyout?.Hide();

        if (_visibleCount == _layout.Tags.Length)
            return;
        _visibleCount = _layout.Tags.Length;
        if (_visibleCount < _tags.Length)
            more.Content = $"+{_tags.Length - _visibleCount}";
        if (_expanded is not null)
            FillOverflow(_expanded);
    }

    private void SyncTagChips()
    {
        while (_chips.Count > _layout.Tags.Length)
        {
            Children.Remove(_chips[^1]);
            _chips.RemoveAt(_chips.Count - 1);
        }
        for (var i = 0; i < _layout.Tags.Length; i++)
        {
            if (i == _chips.Count)
            {
                var chip = CreateChip(_tags[i]);
                chip.Margin = new Thickness(4, 0, 0, 0);
                _chips.Add(chip);
                Children.Insert(i, chip);
            }
            var slot = _layout.Tags[i];
            _chips[i].Measure(new Size(slot.Width, slot.Height));
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_more is null)
            return finalSize;

        var top = Math.Max(0, (finalSize.Height - _layout.Height) / 2);
        for (var i = 0; i < _layout.Tags.Length; i++)
            ArrangeAt(_chips[i], _layout.Tags[i], top);
        if (_layout.More is { } slot)
            ArrangeAt(_more, slot, top);
        return finalSize;
    }

    private static void ArrangeAt(UIElement child, HistoryTagPlacement slot, double top) =>
        child.Arrange(new Rect(slot.X, top + slot.Y, slot.Width, slot.Height));

    private void FillOverflow(WrapPanel panel)
    {
        panel.Children.Clear();
        for (var i = _visibleCount; i < _tags.Length; i++)
            panel.Children.Add(CreateChip(_tags[i]));
    }

    private void OpenOverflow(object sender, RoutedEventArgs _)
    {
        if (sender is not Button trigger || trigger != _more || !trigger.IsLoaded || !IsTriggerInView(trigger) ||
            _flyout is not null || _visibleCount < 0 || _visibleCount >= _tags.Length || ViewModel is not { } vm)
            return;
        var tags = new WrapPanel { MaxWidth = 224, HorizontalSpacing = 5, VerticalSpacing = 5 };
        FillOverflow(tags);
        var presenter = new Style(typeof(FlyoutPresenter));
        presenter.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8)));
        presenter.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 0d));
        var flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
            ShowMode = FlyoutShowMode.Transient,
            FlyoutPresenterStyle = presenter,
            Content = new ScrollViewer
            {
                Content = tags,
                MaxHeight = 168,
                HorizontalScrollMode = ScrollMode.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            }
        };
        _flyout = flyout;
        _expanded = tags;
        vm.PropertyChanged += OnSelectionChanged;
        void Release()
        {
            vm.PropertyChanged -= OnSelectionChanged;
            _flyout = null;
            _expanded = null;
        }
        flyout.Closed += (_, _) => Release();
        try
        {
            flyout.ShowAt(trigger);
        }
        catch
        {
            Release();
            throw;
        }
    }

    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HistoryViewModel.SelectedItem))
            _flyout?.Hide();
    }

    private void TriggerViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs e)
    {
        if (sender != _more)
            return;
        _effectiveViewport = e.EffectiveViewport;
        if (!IsTriggerInView(sender))
            _flyout?.Hide();
    }

    private bool IsTriggerInView(FrameworkElement trigger) => _effectiveViewport is { } viewport &&
        !viewport.IsEmpty && viewport.Right > 0 && viewport.Bottom > 0 &&
        viewport.Left < trigger.ActualWidth && viewport.Top < trigger.ActualHeight;
}
