using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;

namespace SyncClipboard.Updater;

internal sealed class UpdateWindow : Window
{
    public UpdateWindow(bool smokeTest, IClassicDesktopStyleApplicationLifetime desktop)
    {
        Title = UpdaterText.Current.Title;
        Width = 520;
        Height = 180;
        CanResize = false;
        Background = Brushes.White;
        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = UpdaterText.Current.Title, FontSize = 18, Foreground = Brushes.Black },
                new TextBlock
                {
                    Text = UpdaterText.Current.InstallationNotImplemented,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brushes.Black
                }
            }
        };
        if (smokeTest)
        {
            Opened += async (_, _) =>
            {
                await Task.Delay(1000);
                Console.WriteLine("GUI_SMOKE=PASS");
                desktop.Shutdown(0);
            };
        }
    }
}
