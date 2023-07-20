using SyncClipboard.Core.Interfaces;

namespace SyncClipboard.Core.AbstractClasses;

public abstract class TrayIconBase<IconType> : ITrayIcon where IconType : class
{
    public abstract event Action MainWindowWakedUp;
    public abstract void Create();

    #region Icon Animatinon
    private const int ANIMATED_ICON_DELAY_TIME = 150;
    private Timer? _iconTimer;
    private int _iconIndex = 1;
    private bool _isShowingDanamicIcon;
    private IconType[]? _dynamicIcons;
    private IconType? @staticIcon = null;
    private IconType StaticIcon { get => @staticIcon ?? DefaultIcon; set => @staticIcon = value; }
    private readonly Dictionary<string, string> _statusList = new();

    protected abstract IconType DefaultIcon { get; }
    protected abstract IconType ErrorIcon { get; }
    protected abstract int MaxToolTipLenth { get; }

    protected abstract void SetIcon(IconType icon);
    protected abstract void SetToolTip(string text);
    protected abstract IconType[] UploadIcons();
    protected abstract IconType[] DownloadIcons();
    #endregion

    public void ShowDownloadAnimation()
    {
        SetDynamicNotifyIcon(DownloadIcons(), ANIMATED_ICON_DELAY_TIME);
    }

    public void ShowUploadAnimation()
    {
        SetDynamicNotifyIcon(UploadIcons(), ANIMATED_ICON_DELAY_TIME);
    }

    private void SetDynamicNotifyIcon(IconType[] icons, int delayTime)
    {
        if (icons.Length == 0)
        {
            return;
        }

        StopAnimation();

        _dynamicIcons = icons;
        SetIcon(_dynamicIcons[0]);
        _isShowingDanamicIcon = true;
        _iconTimer = new Timer(SetNextDynamicNotifyIcon, null, 0, delayTime);
    }

    public void StopAnimation()
    {
        _iconTimer?.Dispose();
        _iconTimer = null;

        _dynamicIcons = null;
        _iconIndex = 1;
        _isShowingDanamicIcon = false;
        SetStaticIcon();
    }

    private void SetNextDynamicNotifyIcon(object? _)
    {
        if (_dynamicIcons is null || _dynamicIcons.Length == 0)
        {
            return;
        }

        if (_iconIndex >= _dynamicIcons.Length)
        {
            _iconIndex = 0;
        }
        SetIcon(_dynamicIcons[_iconIndex]);
        _iconIndex++;
    }

    public void SetStatusString(string key, string statusStr, bool error)
    {
        SetStatusString(key, statusStr);

        if (error)
        {
            StaticIcon = ErrorIcon;
        }
        else
        {
            StaticIcon = DefaultIcon;
        }
        SetStaticIcon();
    }

    public void SetStatusString(string key, string statusStr)
    {
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
        var eachMaxLenth = MaxToolTipLenth / _statusList.Count;

        var ajustedList = _statusList.Select(status =>
        {
            var oneServiceStr = $"{status.Key}: {status.Value}";
            if (oneServiceStr.Length > eachMaxLenth)
            {
                oneServiceStr = oneServiceStr[..(eachMaxLenth - 1)];
            }
            return oneServiceStr;
        });

        SetToolTip(string.Join(Environment.NewLine, ajustedList));
    }
}
