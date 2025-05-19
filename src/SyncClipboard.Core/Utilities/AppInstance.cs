using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using System.Diagnostics;
using System.IO.Pipes;

namespace SyncClipboard.Core.Utilities;

public sealed class AppInstance(IMainWindow window, ILogger logger) : IDisposable
{
    private const string ActiveCommand = "Active";
    private static readonly string MutexPrefix = @$"Global\{Env.SoftName}-Mutex-{Environment.UserName}-";
    private static readonly string PipePrefix = @$"Global\{Env.SoftName}-Pipe-{Environment.UserName}-";
    private bool _disposed = false;

    private CancellationTokenSource? _cancellationSource = null;

    ~AppInstance() => Dispose();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        CancelWaitForOtherInstanceToActiveAsync();
        GC.SuppressFinalize(this);
        _disposed = true;
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
                var command = await reader.ReadLineAsync().WaitAsync(cancellationToken);

                if (command is ActiveCommand)
                {
                    window?.Show();
                }
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

    private static async Task ActiveOtherInstance()
    {
        using var pipeClient = new NamedPipeClientStream(".", PipePrefix, PipeDirection.InOut);
        var token = new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token;
        try
        {
            await pipeClient.ConnectAsync(token);
            using var writer = new StreamWriter(pipeClient);
            writer.WriteLine(ActiveCommand);
            writer.Flush();
        }
        catch (Exception ex)
        {
            Trace.WriteLine("Failed to call the existed instance, ex is " + ex.Message);
        }
    }

    public static Mutex? EnsureSingleInstance()
    {
        Mutex mutex = new(false, MutexPrefix, out bool createdNew);
        if (!createdNew)
        {
            ActiveOtherInstance().Wait();
            return null;
        }
        return mutex;
    }
}
