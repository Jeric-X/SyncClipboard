using AppKit;
using Avalonia.Controls;
using ObjCRuntime;
using SyncClipboard.Desktop.MacOS.Utilities;

namespace SyncClipboard.Desktop.MacOS.Views;

public class HistoryWindow : Desktop.Views.HistoryWindow
{
    private bool _collectionBehaviorSet = false;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        this.RemoveWindow();
        base.OnClosing(e);
    }

    protected override void FocusOnScreen()
    {
        // 只在第一次显示时设置窗口的 collectionBehavior，使其在当前虚拟桌面显示
        if (!_collectionBehaviorSet)
        {
            SetWindowCollectionBehaviorForCurrentSpace();
            _collectionBehaviorSet = true;
        }

        this.AddWindow();
        // this.FocusMenuBar();
        base.FocusOnScreen();
    }

    private void SetWindowCollectionBehaviorForCurrentSpace()
    {
        // 获取底层的 NSWindow
        if (this.TryGetPlatformHandle() is { HandleDescriptor: "NSWindow" } platformHandle)
        {
            var nsWindow = Runtime.GetNSObject<NSWindow>(platformHandle.Handle);
            if (nsWindow != null)
            {
                // 设置窗口的 collectionBehavior，使其在当前虚拟桌面显示
                // moveToActiveSpace: 当窗口激活时，移动到当前活动的 Space，而不是切换 Space
                nsWindow.CollectionBehavior = NSWindowCollectionBehavior.MoveToActiveSpace;
            }
        }
    }
}
