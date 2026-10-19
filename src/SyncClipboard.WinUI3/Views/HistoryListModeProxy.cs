using System.ComponentModel;

namespace SyncClipboard.WinUI3.Views;

public class HistoryListModeProxy : INotifyPropertyChanged
{
    public static HistoryListModeProxy Current { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _isCompactListMode;
    private bool _isMultiSelecting;

    public bool IsCompactListMode
    {
        get => _isCompactListMode;
        set
        {
            if (_isCompactListMode == value)
            {
                return;
            }

            _isCompactListMode = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCompactListMode)));
        }
    }

    public bool IsMultiSelecting
    {
        get => _isMultiSelecting;
        set
        {
            if (_isMultiSelecting == value)
            {
                return;
            }

            _isMultiSelecting = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMultiSelecting)));
        }
    }
}
