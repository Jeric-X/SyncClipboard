using AppKit;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using ObjCRuntime;
using SyncClipboard.Core.Models;
using SyncClipboard.Desktop.MacOS.Utilities;

namespace SyncClipboard.Desktop.MacOS.Views;

public class HistoryWindow : Desktop.Views.HistoryWindow
{
    private bool _collectionBehaviorSet = false;

    static HistoryWindow()
    {
        PopupRoot.ParentProperty.Changed.AddClassHandler<PopupRoot>((popup, _) =>
        {
            if (popup.ParentTopLevel is not HistoryWindow owner
                || popup.TryGetPlatformHandle() is not { HandleDescriptor: "NSWindow" } popupHandle
                || Runtime.GetNSObject<NSWindow>(popupHandle.Handle) is not { } nativePopup)
            {
                return;
            }

            if (popup.Parent is null)
            {
                nativePopup.ParentWindow?.RemoveChildWindow(nativePopup);
            }
            else if (owner.TryGetPlatformHandle() is { HandleDescriptor: "NSWindow" } ownerHandle
                && Runtime.GetNSObject<NSWindow>(ownerHandle.Handle) is { } nativeOwner)
            {
                // Avalonia 未建立原生父子窗口关系，弹窗需要跟随历史面板进入其他应用的全屏 Space。
                nativeOwner.AddChildWindow(nativePopup, NSWindowOrderingMode.Above);
            }
        });
    }

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
            // 按临时面板管理窗口，使其在全屏 Space 销毁后重新显示时恢复 Space 归属。
            nsWindow?.CollectionBehavior = NSWindowCollectionBehavior.CanJoinAllSpaces
                | NSWindowCollectionBehavior.Transient
                | NSWindowCollectionBehavior.FullScreenAuxiliary;
        }
    }
}
