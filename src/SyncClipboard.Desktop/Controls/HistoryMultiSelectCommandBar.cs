using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using System;
using System.Linq;

namespace SyncClipboard.Desktop.Controls;

// The history bar has a fixed set of commands. Avalonia's dynamic overflow estimates every
// right-labelled button at 102 pixels; partition these commands using their actual widths instead.
public sealed class HistoryMultiSelectCommandBar : CommandBar
{
    private CommandBarButton[]? _commands;
    private Control? _contentHost;
    private Button? _more;
    private ItemsControl? _primaryHost;

    public HistoryMultiSelectCommandBar()
    {
        AddHandler(Button.ClickEvent, OnCommandClick, handledEventsToo: true);
    }

    protected override Type StyleKeyOverride => typeof(CommandBar);

    private void OnCommandClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is CommandBarButton { IsInOverflow: true })
            SetCurrentValue(IsOpenProperty, false);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _contentHost = e.NameScope.Find<Control>("PART_ContentPresenter");
        _more = e.NameScope.Find<Button>("PART_OverflowButton");
        _primaryHost = e.NameScope.Find<ItemsControl>("PART_PrimaryCommands");
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_commands is null)
        {
            _commands = PrimaryCommands.Cast<CommandBarButton>().ToArray();
            base.MeasureOverride(Size.Infinity);
        }

        _contentHost?.Measure(Size.Infinity);
        var availableWidth = Math.Max(0, availableSize.Width - Padding.Left - Padding.Right -
            (_contentHost?.DesiredSize.Width ?? 0));
        var widths = _commands.Select(MeasureInlineWidth).ToArray();
        var spacing = (_primaryHost?.ItemsPanelRoot as StackPanel)?.Spacing ?? 0;
        var visibleCount = widths.Length;
        if (widths.Sum() + (Math.Max(0, widths.Length - 1) * spacing) > availableWidth)
        {
            availableWidth -= MeasureMoreWidth();
            visibleCount = CountFittingCommands(widths, spacing, availableWidth);
        }

        if (visibleCount != PrimaryCommands.Count)
        {
            SecondaryCommands.Clear();
            PrimaryCommands.Clear();
            for (var i = 0; i < _commands.Length; i++)
                (i < visibleCount ? PrimaryCommands : SecondaryCommands).Add(_commands[i]);
        }
        return base.MeasureOverride(availableSize);
    }

    private static double MeasureInlineWidth(CommandBarButton button)
    {
        var inOverflow = button.IsInOverflow;
        button.IsInOverflow = false;
        button.ApplyStyling();
        button.ApplyTemplate();
        button.InvalidateMeasure();
        button.Measure(Size.Infinity);
        var width = button.DesiredSize.Width;
        button.IsInOverflow = inOverflow;
        return width;
    }

    private double MeasureMoreWidth()
    {
        if (_more is null)
            return 0;
        var visible = _more.IsVisible;
        _more.SetCurrentValue(IsVisibleProperty, true);
        _more.Measure(Size.Infinity);
        var width = _more.DesiredSize.Width;
        _more.SetCurrentValue(IsVisibleProperty, visible);
        return width;
    }

    private static int CountFittingCommands(double[] widths, double spacing, double availableWidth)
    {
        var count = 0;
        foreach (var width in widths)
        {
            var required = width + (count > 0 ? spacing : 0);
            if (required > availableWidth)
                break;
            availableWidth -= required;
            count++;
        }
        return count;
    }
}
