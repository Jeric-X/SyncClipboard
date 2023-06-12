using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SyncClipboard.WinUI3.ViewModels
{
    internal class MainWindowViewModel
    {
        public class Item
        {
            public string Tag { get; set; } = "";
            public string Text { get; set; } = "";
        }
        public string Text { get; set; } = "avc";
        public List<NavigationViewItem> ItemCollection { get; set; } = new();

        public MainWindowViewModel()
        {
            ItemCollection.Add(new() { Tag = "123", Content = "456" });
            ItemCollection.Add(new() { Tag = "ddd", Content = "ccc" });
        }
    }
}
