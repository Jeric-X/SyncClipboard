using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Abstract;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
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
    public abstract string ShowcaseText();
    public abstract HistoryRecord CreateHistoryRecord();

    protected abstract IClipboardSetter<Profile> ClipboardSetter { get; }
    protected abstract bool Same(Profile rhs);
    protected abstract ClipboardMetaInfomation CreateMetaInformation();

    #endregion

    protected static string LocalTemplateFolder => Env.TemplateFileFolder;
    protected static IServiceProvider ServiceProvider { get; } = AppCore.Current.Services;
    protected static ILogger Logger => ServiceProvider.GetRequiredService<ILogger>();
    protected static ConfigManager Config => ServiceProvider.GetRequiredService<ConfigManager>();

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

    public virtual Task CheckDownloadedData(CancellationToken token) => Task.CompletedTask;
    public virtual Task<bool> ValidLocalData(bool quick, CancellationToken token) => Task.FromResult(true);
    public virtual bool HasDataFile => false;
    public virtual bool RequiresPrepareData => false;
    public virtual Task PrepareDataAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public virtual string GetLocalDataPath() => string.Empty;

    public async Task SetLocalClipboard(CancellationToken ctk, bool mutex = true)
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
}
