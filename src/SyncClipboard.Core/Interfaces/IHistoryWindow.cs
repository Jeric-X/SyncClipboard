using SyncClipboard.Core.ViewModels.Sub;

namespace SyncClipboard.Core.Interfaces;

public interface IHistoryWindow : IWindow
{
    void FocusSearch();
    void ShowTagPicker();
    void ShowTitleEditor();
    void NavigateTagFilter(bool forward);
    void NavigateTagFilterVertically(bool down);
    void ToggleTagBarExpanded();
    IReadOnlyList<HistoryRecordVM> GetVisibleHistoryRecords();
    void ScrollToTop();
    void ScrollToSelectedItem();
    bool GetScrollViewMetrics(out double offsetY, out double viewportHeight, out double extentHeight);
}
