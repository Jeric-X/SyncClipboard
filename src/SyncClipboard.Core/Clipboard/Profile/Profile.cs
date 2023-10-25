using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Abstract;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using System.Text.Json;

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

    protected const string RemoteProfilePath = Env.RemoteProfilePath;
    protected readonly static string LocalTemplateFolder = Env.TemplateFileFolder;

    private readonly SynchronizationContext? MainThreadSynContext = SynchronizationContext.Current;
    private INotification NotificationManager => ServiceProvider.GetRequiredService<INotification>();
    private bool? EnableNotify => ServiceProvider.GetRequiredService<ConfigManager>().GetConfig<SyncConfig>(ConfigKey.Sync)?.NotifyOnDownloaded;

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

    protected virtual void SetNotification(INotification notificationManager)
    {
        notificationManager.SendText(I18n.Strings.ClipboardUpdated, Text);
    }

    public void SetLocalClipboard(bool notify, CancellationToken ctk)
    {
        var ClipboardObjectContainer = ClipboardSetter.CreateClipboardObjectContainer(MetaInfomation);
        if (ClipboardObjectContainer is null)
        {
            return;
        }

        if (MainThreadSynContext == SynchronizationContext.Current)
        {
            ClipboardSetter.SetLocalClipboard(ClipboardObjectContainer, ctk);
        }
        else
        {
            MainThreadSynContext?.Post((_) => ClipboardSetter.SetLocalClipboard(ClipboardObjectContainer, ctk), null);
        }

        if (notify && (EnableNotify ?? true))
        {
            SetNotification(NotificationManager);
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
        return new Button(I18n.Strings.Copy, new Callbacker(Guid.NewGuid().ToString(), (_) => SetLocalClipboard(false, CancellationToken.None)));
    }
}
