using AppKit;
using Avalonia.Controls;
using ObjCRuntime;
using SyncClipboard.Core.Models;
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

    public override void Show(bool activate)
    {
        // 只在第一次显示时设置窗口的 collectionBehavior，使其在所有虚拟桌面显示
        if (!_collectionBehaviorSet)
        {
            SetWindowCollectionBehaviorForAllSpaces();
            _collectionBehaviorSet = true;
        }

        this.AddWindow();
        // this.FocusMenuBar();
        base.Show(activate);
    }

    public override void Hide()
    {
        this.RemoveWindow();
        base.Hide();
    }

    public override NativeWindowInfo? GetNativeWindowInfo()
    {
        if (base.GetNativeWindowInfo() is not MacNativeWindowInfo nativeWindow
            || this.TryGetPlatformHandle() is not { HandleDescriptor: "NSWindow" } platformHandle)
        {
            return null;
        }

        var nsWindow = Runtime.GetNSObject<NSWindow>(platformHandle.Handle);
        return nsWindow is null
            ? nativeWindow
            : nativeWindow with { WindowNumber = nsWindow.WindowNumber };
    }

    private void SetWindowCollectionBehaviorForAllSpaces()
    {
        // 获取底层的 NSWindow
        if (this.TryGetPlatformHandle() is { HandleDescriptor: "NSWindow" } platformHandle)
        {
            var nsWindow = Runtime.GetNSObject<NSWindow>(platformHandle.Handle);
            // 设置窗口的 collectionBehavior，使其在所有虚拟桌面显示
            // CanJoinAllSpaces: 窗口在所有 Space 中可见
            nsWindow?.CollectionBehavior = NSWindowCollectionBehavior.CanJoinAllSpaces;
        }
    }
}
