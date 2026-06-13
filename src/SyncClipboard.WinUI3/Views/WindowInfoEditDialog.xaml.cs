using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SyncClipboard.Core.Models;

namespace SyncClipboard.WinUI3.Views;

public sealed partial class WindowInfoEditDialog : ContentDialog
{
    public string ProcessName
    {
        get => _ProcessName.Text;
        set => _ProcessName.Text = value;
    }

    public string WindowTitle
    {
        get => _WindowTitle.Text;
        set => _WindowTitle.Text = value;
    }

    public string ExecutableName
    {
        get => _ExecutableName.Text;
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

    private void OnTextChanged(object _, TextChangedEventArgs _1)
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
