using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using SyncClipboard.Core.Models;
using System;

namespace SyncClipboard.Desktop.Views;

public partial class WindowInfoEditDialog : ContentDialog
{
    protected override Type StyleKeyOverride => typeof(ContentDialog);

    public string ProcessName
    {
        get => _ProcessName.Text ?? "";
        set => _ProcessName.Text = value;
    }

    public string WindowTitle
    {
        get => _WindowTitle.Text ?? "";
        set => _WindowTitle.Text = value;
    }

    public string ExecutableName
    {
        get => _ExecutableName.Text ?? "";
        set => _ExecutableName.Text = value;
    }

    public ForegroundWindowInfo GetWindowInfo() => new()
    {
        ProcessName = ProcessName,
        WindowTitle = WindowTitle,
        ExecutableName = ExecutableName
    };

    public void SetWindowInfo(ForegroundWindowInfo info)
    {
        ProcessName = info.ProcessName ?? "";
        WindowTitle = info.WindowTitle ?? "";
        ExecutableName = info.ExecutableName ?? "";
        UpdatePrimaryButtonState();
    }

    public WindowInfoEditDialog()
    {
        InitializeComponent();
        UpdatePrimaryButtonState();
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdatePrimaryButtonState();
    }

    private void UpdatePrimaryButtonState()
    {
        IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(ProcessName) ||
                                 !string.IsNullOrWhiteSpace(WindowTitle) ||
                                 !string.IsNullOrWhiteSpace(ExecutableName);
    }
}
