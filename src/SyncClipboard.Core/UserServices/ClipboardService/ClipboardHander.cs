using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.UserServices.ClipboardService;

abstract public class ClipboardHander : Service
{
    public abstract string SERVICE_NAME { get; }
    public abstract string LOG_TAG { get; }
    protected abstract bool SwitchOn { get; set; }
    protected ILogger Logger { get; }

    private readonly IServiceProvider sp = AppCore.Current.Services;
    private IClipboardChangingListener ClipboardChangingListener { get; }
    private IContextMenu ContextMenu => sp.GetRequiredService<IContextMenu>();
    private ToggleMenuItem? ToggleMenuItem { get; set; }
    protected virtual bool EnableToggleMenuItem => true;
    protected string? ContextMenuGroupName { get; init; }

    public ClipboardHander()
    {
        sp = AppCore.Current.Services;
        Logger = sp.GetRequiredService<ILogger>();
        ClipboardChangingListener = sp.GetRequiredService<IClipboardChangingListener>();
    }

    protected override void StartService()
    {
        ClipboardChangingListener.Changed += ClipBoardChangedHandler;

        Logger.Write(LOG_TAG, $"Service: {SERVICE_NAME} started");
        if (EnableToggleMenuItem)
        {
            ToggleMenuItem = new ToggleMenuItem(SERVICE_NAME, false, (status) => SwitchOn = status);
            ContextMenu?.AddMenuItem(ToggleMenuItem, ContextMenuGroupName);
        }
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

    private async void ClipBoardChangedHandler(ClipboardMetaInfomation clipboardMetaInfomation, Profile profile)
    {
        if (SwitchOn)
        {
            CancellationToken cancelToken = StopPreviousAndGetNewToken();
            try
            {
                await HandleClipboard(clipboardMetaInfomation, profile, cancelToken);
            }
            catch when (cancelToken.IsCancellationRequested)
            {
                Logger.Write(LOG_TAG, $"Error: {SERVICE_NAME} {nameof(HandleClipboard)}");
            }
        }
    }

    protected abstract Task HandleClipboard(ClipboardMetaInfomation clipboardMetaInfomation, Profile profile, CancellationToken token);
}