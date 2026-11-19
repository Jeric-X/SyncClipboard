using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace SyncClipboard.Core.Utilities;

public sealed class AppInstance(IMainWindow window, ILogger logger, HotkeyManager hotkeyManager) : IDisposable
{
    private const string ActiveCommand = "Active";
    private const string ShutdownCommand = "Shutdown";
    private static readonly string MutexPrefix = @$"Global\{Env.SoftName}-Mutex-{Environment.UserName}-";
    private static readonly string PipePrefix = @$"Global\{Env.SoftName}-Pipe-{Environment.UserName}-";
    private static string MutexName => MutexPrefix + AppLaunchOptions.Current.InstanceSuffix;
    private static string PipeName => AppLaunchOptions.Current.IsSpecifiedDataDirectory ? AppLaunchOptions.Current.PipeName : PipePrefix;
    private static FileStream? _directoryLock;
    private static Mutex? _installationMarker;
    private bool _disposed = false;

    private CancellationTokenSource? _cancellationSource = null;
    private static Mutex? GlobalMutex = null;

    ~AppInstance() => Dispose();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        CancelWaitForOtherInstanceToActiveAsync();
        GC.SuppressFinalize(this);
        _installationMarker?.Dispose();
        _directoryLock?.Dispose();
        GlobalMutex?.ReleaseMutex();
        GlobalMutex?.Dispose();
        _disposed = true;
    }

    private async Task ParseCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            logger.Write("Received empty command, ignoring.");
            return;
        }

        if (command is ActiveCommand)
        {
            window?.Show();
        }
        else if (command is ShutdownCommand)
        {
            await AppCore.Current.ExitAsync();
        }
        else if (command.StartsWith(StartArguments.CommandPrefix))
        {
            var cmdName = command[StartArguments.CommandPrefix.Length..];
            hotkeyManager.RunCommand(cmdName);
        }
        else
        {
            logger.Write($"Unknown command received: {command}");
        }
    }

    public async void WaitForOtherInstanceToActiveAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(AppInstance));

        CancelWaitForOtherInstanceToActiveAsync();
        Interlocked.CompareExchange(ref _cancellationSource, new CancellationTokenSource(), null);

        var cancellationToken = _cancellationSource.Token;
        while (cancellationToken.IsCancellationRequested is not true)
        {
            try
            {
                using var pipeServer = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1);
                await pipeServer.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(pipeServer);
                var command = await reader.ReadLineAsync(cancellationToken);

                await ParseCommand(command);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await logger.WriteAsync(ex.ToString());
            }
            catch (OperationCanceledException)
            {
                await logger.WriteAsync("AppInstance", "Exited Normally");
            }
        }
    }

    public void CancelWaitForOtherInstanceToActiveAsync()
    {
        var cancellationSource = Interlocked.Exchange(ref _cancellationSource, null);
        cancellationSource?.Cancel();
        cancellationSource?.Dispose();
        _cancellationSource = null;
    }

    private static async Task SendCommandToOtherInstance(string command)
    {
        using var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
        var token = new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token;
        try
        {
            await pipeClient.ConnectAsync(token);
            using var writer = new StreamWriter(pipeClient);
            writer.WriteLine(command);
            writer.Flush();
        }
        catch (Exception ex)
        {
            Trace.WriteLine("Failed to call the existed instance, ex is " + ex.Message);
        }
    }

    private static Task ShutdownOtherInstance()
    {
        return SendCommandToOtherInstance(ShutdownCommand);
    }

    private static Task ActiveOtherInstance()
    {
        return SendCommandToOtherInstance(ActiveCommand);
    }

    public static Mutex? CreateMutexOrWakeUp(string[] args)
    {
        Mutex mutex = new(false, MutexName, out bool createdNew);
        if (!createdNew)
        {
            if (args.Any(args => args.StartsWith(StartArguments.CommandPrefix)))
            {
                foreach (var arg in args)
                {
                    if (arg.StartsWith(StartArguments.CommandPrefix))
                    {
                        SendCommandToOtherInstance(arg).Wait();
                    }
                }
            }
            else
            {
                ActiveOtherInstance().Wait();
            }
            mutex.Dispose();
            return null;
        }
        return mutex;
    }

    public static Mutex? ForceCreateMutex()
    {
        Mutex mutex = new(false, MutexName, out bool createdNew);
        if (!createdNew)
        {
            ShutdownOtherInstance().Wait();
        }
        return mutex;
    }

    public static bool EnsureSingleInstance(string[] args)
    {
        try
        {
            AppLaunchOptions.Current.ValidateDataDirectory(AppDomain.CurrentDomain.BaseDirectory);
            return TryAcquireInstance(args);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or TypeInitializationException)
        {
            Console.Error.WriteLine($"Could not start SyncClipboard: {ex.GetBaseException().Message}");
            Environment.ExitCode = 1;
            return false;
        }
    }

    private static bool TryAcquireInstance(string[] args)
    {
        Mutex? mutex;
        if (args.Contains(StartArguments.ShutdownPrivious))
        {
            mutex = ForceCreateMutex();
        }
        else
        {
            mutex = CreateMutexOrWakeUp(args);
        }

        if (mutex is null)
        {
            return false;
        }

        try
        {
            if (mutex.WaitOne(TimeSpan.FromSeconds(10)))
            {
                return HoldDataDirectory(mutex);
            }
            mutex.Dispose();
            return false;
        }
        catch (AbandonedMutexException)
        {
            return HoldDataDirectory(mutex);
        }
    }

    private static string InstallationPath =>
        OperatingSystem.IsLinux() ? Env.GetAppImageExecPath() ?? Env.ProgramDirectory : Env.ProgramDirectory;

    private static string InstallationMarkerName(string programDirectory)
    {
        var path = AppLaunchOptions.NormalizePath(programDirectory);
        if (OperatingSystem.IsWindows())
            path = path.ToUpperInvariant();
        return @"Global\SyncClipboard-Installation-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
    }

    internal static Mutex CreateInstallationMarker(string programDirectory)
        => new(false, InstallationMarkerName(programDirectory) + "-" + Environment.ProcessId);

    internal static bool HasOtherInstances(string programDirectory, int excludedProcessId)
    {
        var prefix = InstallationMarkerName(programDirectory) + "-";
        var found = false;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id != excludedProcessId && Mutex.TryOpenExisting(prefix + process.Id, out var marker))
                {
                    marker.Dispose();
                    found = true;
                }
            }
        }
        return found;
    }

    internal static void CheckCanInstallUpdate()
    {
        if (HasOtherInstances(InstallationPath, Environment.ProcessId))
            throw new InvalidOperationException("Close the other SyncClipboard instances from this installation before installing an update.");
    }

    internal static FileStream LockDataDirectory(string directory)
        => new(Path.Combine(directory, ".instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    private static bool HoldDataDirectory(Mutex mutex)
    {
        try
        {
            // A file lock also prevents directory aliases or the normal instance from sharing the same database.
            _directoryLock = LockDataDirectory(Env.AppDataDirectory);
            _installationMarker = CreateInstallationMarker(InstallationPath);
            GlobalMutex = mutex;
            return true;
        }
        catch
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
            throw;
        }
    }
}
