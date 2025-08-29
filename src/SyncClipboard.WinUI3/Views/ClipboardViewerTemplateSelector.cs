using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Abstract;
using SyncClipboard.Core.Models;

#nullable disable

namespace SyncClipboard.WinUI3.Views;

public class ClipboardViewerTemplateSelector : DataTemplateSelector
{
    public DataTemplate Normal { get; set; }
    public DataTemplate Image { get; set; }

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
    {
        if (item is HistoryRecord record)
        {
            if (record.Type == ProfileType.Image)
            {
                return Image;
            }
        }
        return Normal;
    }
}