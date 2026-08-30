using Interop.UIAutomationClient;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace SyncClipboard.WinUI3.Win32;

internal sealed class CurrentSelectedContentProvider(
    ILogger logger,
    INativeWindowController foregroundWindowInfoProvider) : ICurrentSelectedContentProvider
{
    private const string Tag = "CurrentSelectedContent";
    private const int MaxAncestorDepth = 8;

    private readonly ILogger _logger = logger;
    private readonly INativeWindowController _foregroundWindowInfoProvider = foregroundWindowInfoProvider;
    private IUIAutomation? _uiAutomation;

    public ClipboardMetaInfomation? GetCurrentSelectedContent()
    {
        try
        {
            var text = TryGetSelectedText();
            if (text is not null)
            {
                _logger.Write(Tag, $"Read selected text from UI Automation, length={text.Length}");
                return CreateMetaInformation(text: text);
            }

            var files = TryGetSelectedExplorerItems();
            if (files.Length > 0)
            {
                _logger.Write(Tag, $"Read selected file system items from File Explorer, count={files.Length}");
                return CreateMetaInformation(files: files);
            }

            _logger.Write(Tag, "No selected text or file system items were found");
        }
        catch (COMException ex)
        {
            _logger.Write(Tag, $"COM error: {ex.Message}, HRESULT={ex.ErrorCode:X}");
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Exception: {ex.Message}");
        }

        return null;
    }

    private ClipboardMetaInfomation CreateMetaInformation(string? text = null, string[]? files = null)
    {
        return new ClipboardMetaInfomation
        {
            Text = text,
            Files = files,
            Owner = _foregroundWindowInfoProvider.GetForegroundWindowInfo()
        };
    }

    private string? TryGetSelectedText()
    {
        try
        {
            _uiAutomation ??= new CUIAutomation8();
            var element = _uiAutomation.GetFocusedElement();
            var walker = _uiAutomation.RawViewWalker;

            for (var depth = 0; element is not null && depth < MaxAncestorDepth; depth++)
            {
                var text = TryGetSelectedText(element);
                if (text is not null)
                {
                    return text;
                }

                element = walker.GetParentElement(element);
            }
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Failed to read selected text: {ex.Message}");
        }

        return null;
    }

    private static string? TryGetSelectedText(IUIAutomationElement element)
    {
        object? pattern;
        try
        {
            pattern = element.GetCurrentPattern(UIA_PatternIds.UIA_TextPatternId);
        }
        catch (COMException)
        {
            return null;
        }

        if (pattern is not IUIAutomationTextPattern textPattern)
        {
            return null;
        }

        var selection = textPattern.GetSelection();
        if (selection is null || selection.Length == 0)
        {
            return null;
        }

        List<string> selectedParts = [];
        for (var i = 0; i < selection.Length; i++)
        {
            var selectedText = selection.GetElement(i).GetText(-1);
            if (!string.IsNullOrEmpty(selectedText))
            {
                selectedParts.Add(selectedText);
            }
        }

        return selectedParts.Count == 0 ? null : string.Join(Environment.NewLine, selectedParts);
    }

    private string[] TryGetSelectedExplorerItems()
    {
        var foregroundWindow = User32Interop.GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            return [];
        }

        object? shell = null;
        object? shellWindows = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return [];
            }

            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return [];
            }

            shellWindows = ((dynamic)shell).Windows();
            var count = Convert.ToInt32(((dynamic)shellWindows).Count);
            for (var i = 0; i < count; i++)
            {
                object? window = null;
                try
                {
                    window = ((dynamic)shellWindows).Item(i);
                    var windowHandle = new IntPtr(Convert.ToInt64(((dynamic)window).HWND));
                    if (windowHandle != foregroundWindow)
                    {
                        continue;
                    }

                    return GetSelectedItemsFromExplorerWindow(window);
                }
                catch (Exception ex)
                {
                    _logger.Write(Tag, $"Failed to inspect a File Explorer window: {ex.Message}");
                }
                finally
                {
                    ReleaseComObject(window);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Write(Tag, $"Failed to read File Explorer selection: {ex.Message}");
        }
        finally
        {
            ReleaseComObject(shellWindows);
            ReleaseComObject(shell);
        }

        return [];
    }

    private static string[] GetSelectedItemsFromExplorerWindow(object window)
    {
        object? document = null;
        object? selectedItems = null;
        try
        {
            document = ((dynamic)window).Document;
            selectedItems = ((dynamic)document).SelectedItems();
            var count = Convert.ToInt32(((dynamic)selectedItems).Count);
            List<string> paths = [];

            for (var i = 0; i < count; i++)
            {
                object? item = null;
                try
                {
                    item = ((dynamic)selectedItems).Item(i);
                    var path = Convert.ToString(((dynamic)item).Path);
                    if (!string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path)))
                    {
                        paths.Add(path);
                    }
                }
                finally
                {
                    ReleaseComObject(item);
                }
            }

            return [.. paths];
        }
        finally
        {
            ReleaseComObject(selectedItems);
            ReleaseComObject(document);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.FinalReleaseComObject(value);
        }
    }
}
