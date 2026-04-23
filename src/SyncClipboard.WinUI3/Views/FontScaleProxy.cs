using System;
using System.ComponentModel;

namespace SyncClipboard.WinUI3.Views;

/// <summary>
/// 共享字体缩放代理，作为 XAML DataTemplate 中的绑定源
/// </summary>
public class FontScaleProxy : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private double _scale = 1.0;

    /// <summary>文字基准 12px * 缩放比例</summary>
    public double FontSize => 12.0 * _scale;

    public void SetScale(double scale)
    {
        if (Math.Abs(_scale - scale) > 0.001)
        {
            _scale = scale;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FontSize)));
        }
    }
}
