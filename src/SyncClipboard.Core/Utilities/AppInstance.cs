﻿using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using System.Diagnostics;
using System.IO.Pipes;

namespace SyncClipboard.Core.Utilities;

public sealed class AppInstance(IMainWindow window, ILogger logger) : IDisposable
{
    private const string ActiveCommand = "Active";
    private const string ShutdownCommand = "Shutdown";
    private static readonly string MutexPrefix = @$"Global\{Env.SoftName}-Mutex-{Environment.UserName}-";
    private static readonly string PipePrefix = @$"Global\{Env.SoftName}-Pipe-{Environment.UserName}-";
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
        GlobalMutex?.ReleaseMutex();
        GlobalMutex?.Dispose();
        _disposed = true;
    }

    private void ParseCommand(string? command)
    {
        if (command is ActiveCommand)
        {
            window?.Show();
        }
        else if (command is ShutdownCommand)
        {
            AppCore.Current?.Stop();
            Environment.Exit(0);
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
                using var pipeServer = new NamedPipeServerStream(PipePrefix, PipeDirection.InOut, 1);
                await pipeServer.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(pipeServer);
                var command = await reader.ReadLineAsync(cancellationToken);

                ParseCommand(command);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.Write(ex.ToString());
            }
            catch (OperationCanceledException)
            {
                logger.Write("AppInstance", "Exited Normally");
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
        using var pipeClient = new NamedPipeClientStream(".", PipePrefix, PipeDirection.InOut);
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

    public static Mutex? CreateMutexOrWakeUp()
    {
        Mutex mutex = new(false, MutexPrefix, out bool createdNew);
        if (!createdNew)
        {
            ActiveOtherInstance().Wait();
            mutex.Dispose();
            return null;
        }
        return mutex;
    }

    public static Mutex? ForceCreateMutex()
    {
        Mutex mutex = new(false, MutexPrefix, out bool createdNew);
        if (!createdNew)
        {
            ShutdownOtherInstance().Wait();
        }
        return mutex;
    }

    public static bool EnsureSingleInstance(string[] args)
    {
        Mutex? mutex;
        if (args.Contains(StartArguments.ShutdownPrivious))
        {
            mutex = ForceCreateMutex();
        }
        else
        {
            mutex = CreateMutexOrWakeUp();
        }

        if (mutex is null)
        {
            return false;
        }

        try
        {
            if (mutex.WaitOne(TimeSpan.FromSeconds(10)))
            {
                GlobalMutex = mutex;
                return true;
            }
            return false;
        }
        catch (AbandonedMutexException)
        {
            GlobalMutex = CreateMutexOrWakeUp();
            return GlobalMutex?.WaitOne(TimeSpan.FromSeconds(10)) ?? false;
        }
    }
}
