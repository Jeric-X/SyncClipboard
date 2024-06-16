namespace SyncClipboard.Abstract.Notification;

public abstract class NotificationSessionBase<NotificationIdType> : INotificationSession where NotificationIdType : notnull
{
    public abstract string Title { get; set; }
    public TimeSpan? Duration { get; set; }
    public Uri? Image { get; set; }
    public List<Button> Buttons { get; set; } = new();

    protected abstract NotificationIdType? NativeNotificationId { get; }
    protected abstract void NativeRemove();
    protected abstract void NativeShow();
    protected abstract void NativeShowSilent();

    private CancellationTokenSource? _durationCts;
    private readonly CallbackHandler<NotificationIdType> _callbackHandler;

    protected NotificationSessionBase(CallbackHandler<NotificationIdType> callbackHandler)
    {
        _callbackHandler = callbackHandler;
    }

    public void CancelDurationTask()
    {
        var oldCts = Interlocked.Exchange(ref _durationCts, null);
        oldCts?.Cancel();
        oldCts?.Dispose();
    }

    private CancellationToken CreateNewDurationCtk()
    {
        var cts = new CancellationTokenSource();
        var oldCts = Interlocked.Exchange(ref _durationCts, null);
        oldCts?.Cancel();
        oldCts?.Dispose();
        return cts.Token;
    }

    private async void SetNoficifationDuration()
    {
        if (Duration is not null)
        {
            var token = CreateNewDurationCtk();
            try
            {
                await Task.Delay(Duration.Value, token).ConfigureAwait(false);
                NativeRemoveAndClearCallbackHandler();
            }
            catch { }
        }
    }

    public virtual void Show()
    {
        NativeShow();
        SetNoficifationDuration();
    }

    public virtual void ShowSilent()
    {
        NativeShowSilent();
        SetNoficifationDuration();
    }

    private void NativeRemoveAndClearCallbackHandler()
    {
        NativeRemove();
        if (NativeNotificationId is not null)
            _callbackHandler.OnClosed(NativeNotificationId);
    }

    public virtual void Remove()
    {
        try
        {
            NativeRemoveAndClearCallbackHandler();
        }
        catch
        {
        }
    }
}
