#if EMBED_NATIVE
using System.Diagnostics;
#endif
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace SyncClipboard.Updater.SizeProbe;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
#if EMBED_NATIVE
            var nativeIndex = Array.IndexOf(args, "--native-directory");
            if (nativeIndex < 0)
            {
                using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("native.zip");
                if (payload is not null) return RunExtracted(payload, args);
            }
            else
            {
                if (nativeIndex + 1 >= args.Length) throw new ArgumentException("Missing native directory.");
                RegisterNativeLibraries(args[nativeIndex + 1]);
            }
#else
            RegisterNativeLibraries(AppContext.BaseDirectory);
#endif

            if (args.Contains("--self-test"))
            {
                ProbeOperations.Run();
                Console.WriteLine("SELF_TEST=PASS");
                return 0;
            }

            ProbeApplication.SmokeTest = args.Contains("--smoke-test");
            return AppBuilder.Configure<ProbeApplication>().UsePlatformDetect()
                .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] })
                .With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Software] })
                .With(new AvaloniaNativePlatformOptions { RenderingMode = [AvaloniaNativeRenderingMode.Software] })
                .StartWithClassicDesktopLifetime([]);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

#if EMBED_NATIVE
    private static int RunExtracted(Stream payload, string[] args)
    {
        // The parent owns a fresh private directory; it cleans up after the UI child exits,
        // including on Windows where loaded DLLs cannot be deleted by the child itself.
        var directory = Directory.CreateTempSubdirectory("syncclipboard-size-probe-");
        Console.WriteLine("EXTRACTION_DIRECTORY=" + directory.FullName);
        try
        {
            ZipFile.ExtractToDirectory(payload, directory.FullName);
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            start.ArgumentList.Add("--native-directory");
            start.ArgumentList.Add(directory.FullName);
            using var child = Process.Start(start) ?? throw new IOException("Could not start extracted probe.");
            child.WaitForExit();
            return child.ExitCode;
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
#endif

    private static void RegisterNativeLibraries(string directory)
    {
        IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
        {
            var extension = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
            var path = Path.Combine(directory, name.EndsWith(extension, StringComparison.Ordinal) ? name : name + extension);
            if (!File.Exists(path)) return IntPtr.Zero; // Keep OS libraries on the system loader's normal path.
            var handle = NativeLibrary.Load(path);
            Console.WriteLine("NATIVE_LIBRARY=" + Path.GetFileName(path));
            return handle;
        }

        NativeLibrary.SetDllImportResolver(typeof(SkiaSharp.SKBitmap).Assembly, Resolve);
        NativeLibrary.SetDllImportResolver(typeof(HarfBuzzSharp.Blob).Assembly, Resolve);
        NativeLibrary.SetDllImportResolver(typeof(AvaloniaNativePlatformOptions).Assembly, Resolve);
        NativeLibrary.SetDllImportResolver(typeof(Win32PlatformOptions).Assembly, Resolve);
    }
}

internal sealed class ProbeApplication : Application
{
    public static bool SmokeTest { get; set; }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var status = new TextBlock { Text = "Preparing update / 准备更新", FontSize = 18 };
            var fill = new Border { Background = Brushes.DodgerBlue, Width = 0, HorizontalAlignment = HorizontalAlignment.Left };
            var details = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var window = new Window
            {
                Title = "SyncClipboard updater size probe",
                Width = 480,
                Height = 220,
                Background = Brushes.White,
                Content = new StackPanel
                {
                    Margin = new Thickness(24),
                    Spacing = 16,
                    Children =
                    {
                        status,
                        new Border { Height = 10, Background = Brushes.LightGray, Child = fill },
                        details
                    }
                }
            };
            desktop.MainWindow = window;
            window.Opened += async (_, _) =>
            {
                // Let the actual desktop backend paint before exercising the worker.
                await Task.Delay(500);
                try
                {
                    await Task.Run(() => ProbeOperations.Run(new Progress<string>(message =>
                        Dispatcher.UIThread.Post(() => details.Text = message))));
                    status.Text = "Update test passed / 验证通过";
                    fill.Width = 400;
                    Console.WriteLine("GUI_SMOKE=PASS");
                    if (SmokeTest)
                    {
                        await Task.Delay(500);
                        desktop.Shutdown(0);
                    }
                }
                catch (Exception ex)
                {
                    status.Text = "Update test failed / 验证失败";
                    details.Text = ex.Message;
                    Console.Error.WriteLine(ex);
                    if (SmokeTest) desktop.Shutdown(1);
                }
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}

internal static class ProbeOperations
{
    public static void Run(IProgress<string>? progress = null)
    {
        var directory = Directory.CreateTempSubdirectory("syncclipboard-file-test-");
        try
        {
            var source = Path.Combine(directory.FullName, "源文件 abc.txt");
            var copy = Path.Combine(directory.FullName, "copy.txt");
            var moved = Path.Combine(directory.FullName, "moved.txt");
            File.WriteAllBytes(source, "abc"u8.ToArray());
            using (var stream = File.OpenRead(source))
            {
                var hash = Convert.ToHexString(SHA256.HashData(stream));
                if (hash != "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")
                    throw new InvalidDataException("SHA256 known-vector mismatch.");
            }
            progress?.Report("SHA256 verified; copying and replacing files…");
            File.Copy(source, copy);
            File.WriteAllText(moved, "old version");
            File.Move(copy, moved, overwrite: true);
            if (File.ReadAllText(moved) != "abc" || File.Exists(copy)) throw new IOException("Replacement failed.");

            var zipPath = Path.Combine(directory.FullName, "payload.zip");
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                zip.CreateEntryFromFile(moved, "nested/payload.txt");
            var extracted = Path.Combine(directory.FullName, "stage");
            ZipFile.ExtractToDirectory(zipPath, extracted);
            if (File.ReadAllText(Path.Combine(extracted, "nested", "payload.txt")) != "abc")
                throw new InvalidDataException("ZIP extraction failed.");
            Directory.Move(extracted, Path.Combine(directory.FullName, "installed"));
            File.Delete(moved);
            if (File.Exists(moved)) throw new IOException("Delete failed.");
            progress?.Report("File operations, SHA256 and ZIP passed.");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
