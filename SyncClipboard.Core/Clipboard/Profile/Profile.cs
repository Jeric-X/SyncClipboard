using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.Notification;
using System.Text.Json;
using Button = SyncClipboard.Core.Utilities.Notification.Button;

namespace SyncClipboard.Core.Clipboard;

public abstract class Profile
{
    #region ClipboardProfileDTO Field

    public virtual string FileName { get; set; } = "";
    public virtual string Text { get; set; } = "";
    public string TypeString => ProfileTypeHelper.ClipBoardTypeToString(Type);

    #endregion

    #region abstract

    public abstract ProfileType Type { get; }
    public abstract string ToolTip();
    public abstract Task UploadProfile(IWebDav webdav, CancellationToken cancelToken);

    protected abstract IClipboardSetter<Profile> ClipboardSetter { get; set; }
    protected abstract IServiceProvider ServiceProvider { get; set; }
    protected abstract Task<bool> Same(Profile rhs, CancellationToken cancellationToken);
    protected abstract ClipboardMetaInfomation CreateMetaInformation();

    #endregion

    protected string RemoteProfilePath => ServiceProvider.GetRequiredService<IAppConfig>().RemoteProfilePath;
    protected string LocalTemplateFolder => ServiceProvider.GetRequiredService<IAppConfig>().LocalTemplateFolder;

    private readonly SynchronizationContext? MainThreadSynContext = SynchronizationContext.Current;

    private ClipboardMetaInfomation? @metaInfomation;
    public ClipboardMetaInfomation MetaInfomation
    {
        get
        {
            @metaInfomation ??= CreateMetaInformation();
            return @metaInfomation;
        }
    }

    public virtual Task BeforeSetLocal(CancellationToken cancelToken,
        IProgress<HttpDownloadProgress>? progress = null)
    {
        return Task.CompletedTask;
    }

    protected virtual void SetNotification(NotificationManager notificationManager)
    {
        notificationManager.SendText("剪切板同步成功", Text);
    }

    public void SetLocalClipboard(NotificationManager? notificationManager = null)
    {
        var ClipboardObjectContainer = ClipboardSetter.CreateClipboardObjectContainer(MetaInfomation);
        if (ClipboardObjectContainer is null)
        {
            return;
        }

        if (MainThreadSynContext == SynchronizationContext.Current)
        {
            ClipboardSetter.SetLocalClipboard(ClipboardObjectContainer);
        }
        else
        {
            MainThreadSynContext?.Send((_) => ClipboardSetter.SetLocalClipboard(ClipboardObjectContainer), null);
        }

        if (notificationManager is not null)
        {
            SetNotification(notificationManager);
        }
    }

    public string ToJsonString()
    {
        ClipboardProfileDTO jsonProfile = new(FileName, Text, TypeString);
        return JsonSerializer.Serialize(jsonProfile);
    }

    public static async Task<bool> Same(Profile? lhs, Profile? rhs, CancellationToken cancellationToken)
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

        return await lhs.Same(rhs, cancellationToken);
    }

    public override string ToString()
    {
        string str = "";
        str += "FileName" + FileName;
        str += "Text:" + Text;
        return str;
    }

    protected Button DefaultButton()
    {
        return new Button("复制", new(Guid.NewGuid().ToString(), (_) => SetLocalClipboard()));
    }
}
