using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.ViewModels;
using System.IO;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SyncClipboard.WinUI3.Views;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class LicensePage : Page
{
    public LicensePage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var path = e.Parameter as string;
        if (path is not null)
        {
            _TextBlock.Text = File.ReadAllText(Path.Combine(Core.Commons.Env.Directory, $"LICENSES/{path}"));
        }
        base.OnNavigatedTo(e);
    }
}
