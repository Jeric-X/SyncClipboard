using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Desktop;
using SyncClipboard.Desktop.ClipboardAva.ClipboardReader;
using SyncClipboard.Desktop.ClipboardAva.ClipboardWriter;

namespace SyncClipboard.Test.Desktop;

[TestClass]
[DoNotParallelize]
public class ClipboardInjectionTests
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions());

    [TestMethod]
    public async Task RegisteredWriterUsesMainWindowClipboardWithoutAppCurrent()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ClipboardInjectionTests));
        await session.Dispatch(async () =>
        {
            var window = new TestWindow();
            try
            {
                var registrations = AppServices.ConfigureServices();
                IServiceCollection services = new ServiceCollection();
                services.Add(registrations.Single(value => value.ServiceType == typeof(IClipboard)));
                services.Add(registrations.Single(value => value.ImplementationType == typeof(AvaloniaClipboardWriter)));
                services.Add(registrations.Single(value => value.ImplementationType == typeof(AvaloniaClipboardReader)));
                services.AddSingleton<IMainWindow>(window);
                using var provider = services.BuildServiceProvider();

                Assert.AreSame(window.Clipboard, provider.GetRequiredService<IClipboard>());
                Assert.AreSame(provider.GetRequiredService<IClipboard>(), provider.GetRequiredService<IClipboard>());
                var writer = provider.GetRequiredService<IClipboardWriter>();
                await writer.SetTextAsync("注入剪贴板", CancellationToken.None);
                var reader = provider.GetRequiredService<IClipboardReader>();
                Assert.AreEqual("注入剪贴板", await reader.GetStringAsync(DataFormat.Text.Identifier, CancellationToken.None));
                await window.Clipboard!.ClearAsync();
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    private sealed class TestWindow : Window, IMainWindow
    {
        public void NavigateTo(PageDefinition page, NavigationTransitionEffect effect, object? para) =>
            throw new NotSupportedException();
        public void OpenPage(PageDefinition page, object? para = null) => throw new NotSupportedException();
        public void NavigateToLastLevel() => throw new NotSupportedException();
        public void NavigateToNextLevel(PageDefinition page, object? para) => throw new NotSupportedException();
    }
}
