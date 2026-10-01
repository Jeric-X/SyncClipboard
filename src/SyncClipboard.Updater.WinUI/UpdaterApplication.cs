using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.XamlTypeInfo;

namespace SyncClipboard.Updater;

internal sealed partial class UpdaterApplication : Application, IXamlMetadataProvider
{
    private readonly string[] arguments;
    private readonly XamlControlsXamlMetaDataProvider metadata = new();
    private UpdateWindow? window;

    public UpdaterApplication(string[] arguments)
    {
        this.arguments = arguments;
        UnhandledException += (_, args) => Console.Error.WriteLine(args.Exception);
    }

    public IXamlType GetXamlType(Type type) => metadata.GetXamlType(type);
    public IXamlType GetXamlType(string fullName) => metadata.GetXamlType(fullName);
    public XmlnsDefinition[] GetXmlnsDefinitions() => metadata.GetXmlnsDefinitions();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            // With no App.xaml, application resources become available at OnLaunched.
            Resources.MergedDictionaries.Add(new XamlControlsResources());
            window = new UpdateWindow(arguments);
            window.Activate();
        }
        catch (Exception error)
        {
            Program.ExitCode = 1;
            Program.ShowFatalError(error, arguments is ["--smoke-test"]);
            Exit();
        }
    }
}
