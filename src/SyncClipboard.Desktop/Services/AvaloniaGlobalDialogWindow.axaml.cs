using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.Services;

public partial class AvaloniaGlobalDialogWindow : Window
{
    private bool _result;

    public AvaloniaGlobalDialogWindow()
    {
        InitializeComponent();
    }

    public AvaloniaGlobalDialogWindow(
        string title,
        string message,
        string? primaryButtonText,
        string closeButtonText) : this()
    {
        Title = title;
        _DialogTitle.Text = title;
        _DialogMessage.Text = message;
        _PrimaryButton.Content = primaryButtonText;
        _PrimaryButton.IsVisible = primaryButtonText is not null;
        _CloseButton.Content = closeButtonText;
        Opened += (_, _) => _CloseButton.Focus();
    }

    public async Task<bool> ShowAsync()
    {
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Closed += (_, _) => closed.TrySetResult();
        Show();
        Activate();
        await closed.Task;
        return _result;
    }

    private void PrimaryButtonClick(object? sender, RoutedEventArgs args)
    {
        _result = true;
        Close();
    }

    private void CloseButtonClick(object? sender, RoutedEventArgs args)
    {
        _result = false;
        Close();
    }
}
