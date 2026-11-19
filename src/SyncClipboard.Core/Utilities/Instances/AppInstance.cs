using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace SyncClipboard.Core.Utilities.Instances;

public sealed class AppInstance(IMainWindow window, ILogger logger, HotkeyManager hotkeyManager) : IDisposable
{
    private const string ActiveCommand = "Active";
    private const string ShutdownCommand = "Shutdown";
    private static readonly string MutexPrefix = @$"Global\{Env.SoftName}-Mutex-{Environment.UserName}-";
    private static readonly string PipePrefix = @$"Global\{Env.SoftName}-Pipe-{Environment.UserName}-";
    private static string MutexName => MutexPrefix + GetInstanceSuffix(AppDataDirectoryOptions.Current);
    private static string PipeName => GetPipeName(AppDataDirectoryOptions.Current);

    internal static string GetInstanceSuffix(AppDataDirectoryOptions options) => options.AppDataDirectory is null ? string.Empty
        : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            OperatingSystem.IsWindows() ? options.AppDataDirectory.ToUpperInvariant() : options.AppDataDirectory)))[..24];

    // Keep the Unix socket name short enough for the standard macOS per-user temporary directory.
    internal static string GetPipeName(AppDataDirectoryOptions options) => !options.IsSpecifiedDataDirectory ? PipePrefix
        : "SC-" + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(Environment.UserName + ":" + GetInstanceSuffix(options))))[..16];

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

    // No exit code means this process owns the instance and should continue startup.
    public static int? EnsureSingleInstance(string[] args)
    {
        try
        {
            AppDataDirectoryOptions.Current.ValidateDataDirectory(AppDomain.CurrentDomain.BaseDirectory);
            return TryAcquireInstance(args) ? null : 0;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or TypeInitializationException)
        {
            Console.Error.WriteLine($"Could not start SyncClipboard: {ex.GetBaseException().Message}");
            return 1;
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
        var path = AppDataDirectoryOptions.NormalizePath(programDirectory);
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
            throw new InvalidOperationException(I18n.Strings.UpdateCloseOtherInstances);
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
