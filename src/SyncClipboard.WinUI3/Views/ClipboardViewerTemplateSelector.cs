using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.ViewModels.Sub;

#nullable disable

namespace SyncClipboard.WinUI3.Views;

public class ClipboardViewerTemplateSelector : DataTemplateSelector
{
    public DataTemplate Normal { get; set; }
    public DataTemplate Image { get; set; }

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
    {
        if (item is HistoryRecordVM record)
        {
            if (record.Type == ProfileType.Image)
            {
                return Image;
            }
        }
        return Normal;
    }
}