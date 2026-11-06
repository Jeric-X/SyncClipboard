using Microsoft.UI.Xaml;

namespace SyncClipboard.Test.WinUI3.App;

public sealed partial class TestApplication : Application
{
    internal Func<Task> RunTest { get; init; } = null!;
    internal int ExitCode { get; private set; } = 1;

    public TestApplication()
    {
        UnhandledException += (_, args) => Console.Error.WriteLine($"{args.Message}: {args.Exception}");
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            await RunTest();
            ExitCode = 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
        }
        finally
        {
            Exit();
        }
    }
}
