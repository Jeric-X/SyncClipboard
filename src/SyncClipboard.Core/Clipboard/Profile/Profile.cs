using Microsoft.Extensions.DependencyInjection;
using NativeNotification.Interface;
using SyncClipboard.Abstract;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using System.Text.Json;

namespace SyncClipboard.Core.Clipboard;

public abstract class Profile
{
    #region ClipboardProfileDTO Field

    public virtual string FileName { get; set; } = "";
    public virtual string Text { get; set; } = "";

    #endregion

    #region abstract

    public abstract ProfileType Type { get; }
    public abstract string ToolTip();
    public abstract string ShowcaseText();
    public abstract HistoryRecord CreateHistoryRecord();

    protected abstract IClipboardSetter<Profile> ClipboardSetter { get; }
    protected abstract bool Same(Profile rhs);
    protected abstract ClipboardMetaInfomation CreateMetaInformation();

    #endregion

    protected const string RemoteProfilePath = Env.RemoteProfilePath;
    protected static string LocalTemplateFolder => Env.TemplateFileFolder;
    protected static IServiceProvider ServiceProvider { get; } = AppCore.Current.Services;
    protected static ILogger Logger => ServiceProvider.GetRequiredService<ILogger>();
    protected static ConfigManager Config => ServiceProvider.GetRequiredService<ConfigManager>();

    private static INotification SharedNotification => ServiceProvider.GetRequiredKeyedService<INotification>("ProfileNotification");
    private static bool EnableNotify => Config.GetConfig<SyncConfig>().NotifyOnDownloaded;

    private ClipboardMetaInfomation? @metaInfomation;
    public ClipboardMetaInfomation MetaInfomation
    {
        get
        {
            @metaInfomation ??= CreateMetaInformation();
            return @metaInfomation;
        }
    }

    public bool ContentControl { get; set; } = true;
    public virtual bool IsAvailableFromRemote() => true;
    public bool IsAvailableFromLocal() => !ContentControl || IsAvailableAfterFilter();
    public virtual bool IsAvailableAfterFilter() => true;

    public virtual Task EnsureAvailable(CancellationToken token) => Task.CompletedTask;

    #region 数据访问接口 - 用于IRemoteClipboardServer

    /// <summary>
    /// 是否有关联的数据文件需要上传/下载
    /// </summary>
    public virtual bool HasDataFile => false;
    
    /// <summary>
    /// 是否需要在上传前预处理数据（如GroupProfile需要打包成zip）
    /// </summary>
    public virtual bool RequiresPrepareData => false;
    
    /// <summary>
    /// 准备数据（如压缩文件等）
    /// </summary>
    public virtual Task PrepareDataAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    
    /// <summary>
    /// 清理准备的数据
    /// </summary>
    public virtual Task CleanupPreparedDataAsync() => Task.CompletedTask;
    
    /// <summary>
    /// 获取本地数据文件路径
    /// </summary>
    public virtual string? GetLocalDataPath() => null;
    
    /// <summary>
    /// 设置本地数据文件路径
    /// </summary>
    public virtual void SetLocalDataPath(string path) { }
    
    /// <summary>
    /// 获取数据流（用于上传）
    /// </summary>
    public virtual Task<Stream?> GetDataStreamAsync() => Task.FromResult<Stream?>(null);
    
    /// <summary>
    /// 保存数据流（用于下载）
    /// </summary>
    public virtual Task SaveDataStreamAsync(Stream stream, CancellationToken cancellationToken = default) => Task.CompletedTask;

    #endregion

    protected virtual void SetNotification(INotification notification)
    {
        notification.Title = I18n.Strings.ClipboardUpdated;
        notification.Message = Text;
        notification.Show();
    }

    private static void ResetNotification(INotification notification)
    {
        notification.Title = string.Empty;
        notification.Message = string.Empty;
        notification.Image = null;
        notification.Buttons = [];
        notification.ContentAction = null;
        notification.Remove();
    }

    public async Task SetLocalClipboard(bool notify, CancellationToken ctk, bool mutex = true)
    {
        if (mutex)
        {
            await LocalClipboard.Semaphore.WaitAsync(ctk);
        }

        try
        {
            var dispather = AppCore.Current.Services.GetService<IThreadDispatcher>();
            if (dispather is null)
            {
                await ClipboardSetter.SetLocalClipboard(MetaInfomation, ctk);
            }
            else
            {
                await dispather.RunOnMainThreadAsync(() => ClipboardSetter.SetLocalClipboard(MetaInfomation, ctk));
            }

            if (notify && EnableNotify)
            {
                Logger.Write("System notification has sent.");
                ResetNotification(SharedNotification);
                SetNotification(SharedNotification);
            }
        }
        finally
        {
            if (mutex)
            {
                LocalClipboard.Semaphore.Release();
            }
        }
    }

    public string ToJsonString() => JsonSerializer.Serialize(ToDto());

    public ClipboardProfileDTO ToDto() => new ClipboardProfileDTO(FileName, Text, Type);

    public static bool Same(Profile? lhs, Profile? rhs)
    {
        if (ReferenceEquals(lhs, rhs))
        {
            return true;
        }

        if (lhs is null)
        {
            return rhs is null;
        }

        if (rhs is null)
        {
            return false;
        }

        if (lhs.GetType() != rhs.GetType())
        {
            return false;
        }

        return lhs.Same(rhs);
    }

    public override string ToString()
    {
        string str = "";
        str += "FileName" + FileName;
        str += "Text:" + Text;
        return str;
    }

    protected ActionButton DefaultButton()
    {
        return new ActionButton(I18n.Strings.Copy, () => { _ = SetLocalClipboard(false, CancellationToken.None); });
    }
}
