using SyncClipboard.Abstract;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Interfaces;

public interface IRemoteClipboardServer : IDisposable
{
    // Profile元数据操作
    Task<ClipboardProfileDTO?> GetProfileAsync(CancellationToken cancellationToken = default);
    Task SetProfileAsync(ClipboardProfileDTO profile, CancellationToken cancellationToken = default);
    Task<Profile> SetBlankProfileAsync(CancellationToken cancellationToken = default);
    
    // Profile数据操作 - 传入Profile对象，由Profile决定文件处理方式
    Task UploadProfileDataAsync(Profile profile, CancellationToken cancellationToken = default);
    Task DownloadProfileDataAsync(Profile profile, IProgress<HttpDownloadProgress>? progress = null, CancellationToken cancellationToken = default);
    Task DeleteProfileDataAsync(Profile profile, CancellationToken cancellationToken = default);
    
    // 连接状态
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
    
    // 远程Profile变更通知事件 - 仅在远程Profile修改时触发
    event EventHandler<ProfileChangedEventArgs>? RemoteProfileChanged;
    
    // 开始轮询远程Profile变化
    Task StartPollingAsync(CancellationToken cancellationToken = default);
    
    // 停止轮询
    void StopPolling();
    
    // 初始化
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public class ProfileChangedEventArgs : EventArgs
{
    public ClipboardProfileDTO? NewProfile { get; init; }
    public ClipboardProfileDTO? OldProfile { get; init; }
}