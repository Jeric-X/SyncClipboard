using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.ViewModels;

namespace SyncClipboard.Core.AbstractClasses;

public abstract class TrayIconBase<IconType> : ITrayIcon where IconType : class
{
    public abstract event Action MainWindowWakedUp;
    public abstract void Create();

    private static IThreadDispatcher Dispatcher => AppCore.Current.Services.GetRequiredService<IThreadDispatcher>();

    #region Icon Animatinon
    private const int ANIMATED_ICON_DELAY_TIME = 150;
    private Timer? _iconTimer;
    private bool _isShowingDanamicIcon;
    private bool _isActive = true;
    private bool _isError = false;
    private IEnumerable<IconType>? _dynamicIcons;
    private IEnumerator<IconType>? _dynamicIconEnumerator;
    private IconType? @staticIcon = null;
    private IconType StaticIcon { get => @staticIcon ?? DefaultIcon; set => @staticIcon = value; }
    private readonly Dictionary<string, string> _statusList = [];

    protected abstract IconType DefaultIcon { get; }
    protected abstract IconType ErrorIcon { get; }
    protected abstract IconType DefaultInactiveIcon { get; }
    protected abstract IconType ErrorInactiveIcon { get; }
    protected abstract int MaxToolTipLenth { get; }

    protected abstract void SetIcon(IconType icon);
    protected abstract void SetToolTip(string text);
    protected abstract IEnumerable<IconType> UploadIcons();
    protected abstract IEnumerable<IconType> DownloadIcons();
    protected virtual ServiceStatusViewModel? ServiceStatusViewModel { get; }
    #endregion

    public void ShowDownloadAnimation()
    {
        Dispatcher.RunOnMainThreadAsync(() => SetDynamicNotifyIcon(DownloadIcons(), ANIMATED_ICON_DELAY_TIME));
    }

    public void ShowUploadAnimation()
    {
        Dispatcher.RunOnMainThreadAsync(() => SetDynamicNotifyIcon(UploadIcons(), ANIMATED_ICON_DELAY_TIME));
    }

    private void SetDynamicNotifyIcon(IEnumerable<IconType> icons, int delayTime)
    {
        if (!icons.Any())
        {
            return;
        }

        StopAnimation();

        lock (this)
        {
            _dynamicIcons = icons;
            _dynamicIconEnumerator = icons.GetEnumerator();
            _isShowingDanamicIcon = true;
            _iconTimer = new Timer(SetNextDynamicNotifyIcon, null, 0, delayTime);
        }
    }

    public void StopAnimation()
    {
        lock (this)
        {
            _iconTimer?.Dispose();
            _iconTimer = null;

            _dynamicIcons = null;
            _dynamicIconEnumerator = null;
            _isShowingDanamicIcon = false;
        }
        SetStaticIcon();
    }

    private void SetNextDynamicNotifyIcon(object? _)
    {
        IconType icon;
        lock (this)
        {
            if (_dynamicIcons is null || _dynamicIconEnumerator is null)
            {
                return;
            }

            if (!_dynamicIconEnumerator.MoveNext())
            {
                _dynamicIconEnumerator = _dynamicIcons.GetEnumerator();
                _dynamicIconEnumerator.MoveNext();
            }
            icon = _dynamicIconEnumerator.Current;
        }
        SetIcon(icon);

        // 在设置Icon的过程中其他线程可能会停止动态图标
        SetStaticIcon();
    }

    public virtual void SetStatusString(string key, string statusStr, bool error)
    {
        SetStatusString(key, statusStr);
        ServiceStatusViewModel?.SetStatusString(key, statusStr, error);

        _isError = error;
        ChooseStaticIcon();
        SetStaticIcon();
    }

    private void ChooseStaticIcon()
    {
        switch (_isError, _isActive)
        {
            case (true, true):
                StaticIcon = ErrorIcon;
                break;
            case (true, false):
                StaticIcon = ErrorInactiveIcon;
                break;
            case (false, true):
                StaticIcon = DefaultIcon;
                break;
            case (false, false):
                StaticIcon = DefaultInactiveIcon;
                break;
        }
    }

    public virtual void SetStatusString(string key, string statusStr)
    {
        ServiceStatusViewModel?.SetStatusString(key, statusStr);

        if (!string.IsNullOrEmpty(key))
        {
            _statusList[key] = statusStr;
        }
        ActiveStatusString();
    }

    private void SetStaticIcon()
    {
        if (!_isShowingDanamicIcon)
        {
            SetIcon(StaticIcon);
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

    public void SetActiveStatus(bool active)
    {
        _isActive = active;
        ChooseStaticIcon();
        SetStaticIcon();
    }

    public void RefreshIcon()
    {
        ChooseStaticIcon();
        SetStaticIcon();
    }
}
