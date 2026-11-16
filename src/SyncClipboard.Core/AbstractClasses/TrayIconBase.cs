using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities.Runner;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Core.AbstractClasses;

public abstract class TrayIconBase<IconType> : ITrayIcon where IconType : class
{
    public event Action? LeftClicked;
    public event Action? DoubleClicked;

    private readonly MultiTimesEventSimulator _clickSimulator = new(TimeSpan.FromMilliseconds(200));

    protected TrayIconBase()
    {
        _clickSimulator[1] = () => LeftClicked?.Invoke();
        _clickSimulator[2] = () => DoubleClicked?.Invoke();
    }

    protected void OnRawLeftClicked() => _clickSimulator.TriggerOriginalEvent();

    public abstract void Create();

    protected static readonly TimeSpan AnimationInterval = TimeSpan.FromMilliseconds(150);
    private IconType[]? _uploadFrames;
    private IconType[]? _downloadFrames;
    private IconType[]? _animationFrames;
    private int _nextFrame;
    private bool _isActive = true;
    private bool _isError;
    private readonly Dictionary<string, string> _statusList = [];

    protected abstract IconType DefaultIcon { get; }
    protected abstract IconType ErrorIcon { get; }
    protected abstract IconType DefaultInactiveIcon { get; }
    protected abstract IconType ErrorInactiveIcon { get; }
    protected abstract int MaxToolTipLenth { get; }

    // Always enqueue, including on the UI thread, so starts, stops and status changes keep their order.
    protected abstract void PostToUIThread(Action action);
    protected abstract void StartAnimationTimer();
    protected abstract void StopAnimationTimer();
    protected abstract void SetIcon(IconType icon);
    protected abstract void SetToolTip(string text);
    protected abstract IEnumerable<IconType> UploadIcons();
    protected abstract IEnumerable<IconType> DownloadIcons();
    protected virtual ServiceStatusViewModel? ServiceStatusViewModel { get; }

    public void ShowDownloadAnimation() => PostToUIThread(() => StartAnimation(_downloadFrames ??= [.. DownloadIcons()]));

    public void ShowUploadAnimation() => PostToUIThread(() => StartAnimation(_uploadFrames ??= [.. UploadIcons()]));

    private void StartAnimation(IconType[] frames)
    {
        if (frames.Length == 0)
        {
            return;
        }

        StopAnimationTimer();
        _animationFrames = frames;
        _nextFrame = 0;
        ShowNextFrame();
        StartAnimationTimer();
    }

    protected void ShowNextFrame()
    {
        if (_animationFrames is null)
        {
            return;
        }

        SetIcon(_animationFrames[_nextFrame]);
        _nextFrame = (_nextFrame + 1) % _animationFrames.Length;
    }

    public void StopAnimation() => PostToUIThread(() =>
    {
        StopAnimationTimer();
        _animationFrames = null;
        SetStaticIcon();
    });

    public virtual void SetStatusString(string key, string statusStr, bool error) => PostToUIThread(() =>
    {
        UpdateStatusString(key, statusStr, error);
        _isError = error;
        SetStaticIcon();
    });

    public virtual void SetStatusString(string key, string statusStr) => PostToUIThread(() => UpdateStatusString(key, statusStr));

    private void UpdateStatusString(string key, string statusStr, bool? error = null)
    {
        ServiceStatusViewModel?.SetStatusString(key, statusStr, error);

        if (!string.IsNullOrEmpty(key))
        {
            _statusList[key] = statusStr;
            ActiveStatusString();
        }
    }

    private void SetStaticIcon()
    {
        if (_animationFrames is null)
        {
            SetIcon((_isError, _isActive) switch
            {
                (true, true) => ErrorIcon,
                (true, false) => ErrorInactiveIcon,
                (false, true) => DefaultIcon,
                (false, false) => DefaultInactiveIcon
            });
        }
    }

    private void ActiveStatusString()
    {
        var eachMaxLenth = (MaxToolTipLenth / _statusList.Count) - 1;

        var ajustedList = _statusList.Select(status =>
        {
            var firstLine = status.Value.Split('\n')[0];
            var oneServiceStr = $"{status.Key}: {firstLine}";
            if (oneServiceStr.Length > eachMaxLenth)
            {
                oneServiceStr = oneServiceStr[..(eachMaxLenth - 1)];
            }
            return oneServiceStr;
        });

        SetToolTip(string.Join(Environment.NewLine, ajustedList));
    }

    public void SetActiveStatus(bool active) => PostToUIThread(() =>
    {
        _isActive = active;
        SetStaticIcon();
    });

    public void RefreshIcon() => PostToUIThread(SetStaticIcon);
}
