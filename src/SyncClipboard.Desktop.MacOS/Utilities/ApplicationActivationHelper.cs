using System.Collections.Generic;
using System.Linq;
using AppKit;
using Avalonia.Controls;

namespace SyncClipboard.Desktop.MacOS.Utilities;

public static class ApplicationActivationHelper
{
    public static HashSet<Window> ActiveWindows { get; } = [];

    public static void AddWindow(this Window window)
    {
        if (ActiveWindows.Count == 0)
        {
            NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        }
        ActiveWindows.Add(window);
    }

    public static void RemoveWindow(this Window window)
    {
        ActiveWindows.Remove(window);
        if (ActiveWindows.Count == 0)
        {
            NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Accessory;
        }
    }

    public static void FocusMenuBar(this Window _)
    {
        // 这是一个workaround，切换NSApplicationActivationPolicy后系统菜单无法被点击，需要先Focus到其他App
        NSRunningApplication.GetRunningApplications("com.apple.dock")
            .FirstOrDefault()?.Activate(NSApplicationActivationOptions.ActivateIgnoringOtherWindows);
    }
}