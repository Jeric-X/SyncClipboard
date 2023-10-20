using SyncClipboard.Core.Interfaces;
using System.Diagnostics;
using System.IO.Pipes;

namespace SyncClipboard.Core.Utilities;

public sealed class AppInstance : IDisposable
{
    private const string ActiveCommand = "Active";

    private readonly string _appId;
    private readonly IMainWindow _mainWindow;
    private readonly ILogger _logger;
    private bool _disposed = false;

    private CancellationTokenSource? _cancellationSource = null;

    public AppInstance(IMainWindow window, ILogger logger, IAppConfig appConfig)
    {
        _appId = appConfig.AppId;
        _mainWindow = window;
        _logger = logger;
    }

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
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AppInstance));
        }

        CancelWaitForOtherInstanceToActiveAsync();
        Interlocked.CompareExchange(ref _cancellationSource, new CancellationTokenSource(), null);

        var cancellationToken = _cancellationSource.Token;
        while (cancellationToken.IsCancellationRequested is not true)
        {
            try
            {
                using var pipeServer = new NamedPipeServerStream(_appId, PipeDirection.InOut, 1);
                await pipeServer.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(pipeServer);
                var command = await reader.ReadLineAsync().WaitAsync(cancellationToken);

                if (command is ActiveCommand)
                {
                    _mainWindow?.Show();
                }
            }
            catch (Exception ex)
            {
                _logger.Write(ex.ToString());
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

    public static async Task ActiveOtherInstance(string appId)
    {
        using var pipeClient = new NamedPipeClientStream(".", appId, PipeDirection.InOut);
        var token = new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token;
        try
        {
            await pipeClient.ConnectAsync(token);
            using var writer = new StreamWriter(pipeClient);
            writer.WriteLine(ActiveCommand);
            writer.Flush();
        }
        catch
        {
            Trace.WriteLine("Failed to call the existed instance");
        }
    }
}
