using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.UserServices;

abstract public class ClipboardHander : Service
{
    protected abstract bool SwitchOn { get; set; }
    protected abstract ILogger Logger { get; }
    public abstract string SERVICE_NAME { get; }
    public abstract string LOG_TAG { get; }

    protected abstract IClipboardChangingListener ClipboardChangingListener { get; }

    protected ToggleMenuItem? ToggleMenuItem { get; set; }
    protected virtual string? ContextMenuGroupName { get; } = null;
    protected abstract IContextMenu? ContextMenu { get; }

    protected override void StartService()
    {
        ClipboardChangingListener.Changed += ClipBoardChangedHandler;

        Logger.Write(LOG_TAG, $"Service: {SERVICE_NAME} started");
        ToggleMenuItem = new ToggleMenuItem(SERVICE_NAME, false, (status) => SwitchOn = status);
        ContextMenu?.AddMenuItem(ToggleMenuItem, ContextMenuGroupName);
        Load();
    }

    public void CancelProcess()
    {
        lock (_cancelSourceLocker)
        {
            if (_cancelSource?.Token.CanBeCanceled ?? false)
            {
                _cancelSource.Cancel();
            }
            _cancelSource = null;
        }
    }

    public override void Load()
    {
        if (ToggleMenuItem is not null)
        {
            ToggleMenuItem.Checked = SwitchOn;
        }
    }

    protected override void StopSerivce()
    {
        CancelProcess();
        Logger.Write(LOG_TAG, $"Service: {SERVICE_NAME} stopped");
    }

    private CancellationTokenSource? _cancelSource;
    private readonly object _cancelSourceLocker = new();

    protected virtual CancellationToken StopPreviousAndGetNewToken()
    {
        lock (_cancelSourceLocker)
        {
            if (_cancelSource?.Token.CanBeCanceled ?? false)
            {
                _cancelSource.Cancel();
            }
            _cancelSource = new();
            return _cancelSource.Token;
        }
    }

    private void ClipBoardChangedHandler(ClipboardMetaInfomation clipboardMetaInfomation)
    {
        if (SwitchOn)
        {
            CancellationToken cancelToken = StopPreviousAndGetNewToken();
            HandleClipboard(clipboardMetaInfomation, cancelToken);
        }
    }

    protected abstract void HandleClipboard(ClipboardMetaInfomation clipboardMetaInfomation, CancellationToken cancelToken);
}