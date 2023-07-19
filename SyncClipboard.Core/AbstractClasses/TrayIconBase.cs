using SyncClipboard.Core.Interfaces;

namespace SyncClipboard.Core.AbstractClasses;

public abstract class TrayIconBase<IconType> : ITrayIcon
{
    public abstract event Action MainWindowWakedUp;
    public abstract void Create();

    #region Icon Animatinon
    private const int ANIMATED_ICON_DELAY_TIME = 150;
    private Timer? _iconTimer;
    private int _iconIndex = 1;
    private IconType[]? _dynamicIcons;
    protected bool _isShowingDanamicIcon;

    protected abstract void SetIcon(IconType icon);
    protected abstract void SetDefaultIcon();
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
        SetDefaultIcon();
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
}
