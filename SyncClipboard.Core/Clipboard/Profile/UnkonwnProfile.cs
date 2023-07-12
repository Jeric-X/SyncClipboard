using SyncClipboard.Core.Interfaces;

namespace SyncClipboard.Core.Clipboard;

public class UnkonwnProfile : Profile
{
    public override ProfileType Type => ProfileType.Unknown;

    protected override IClipboardSetter<Profile> ClipboardSetter { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }
    protected override IServiceProvider ServiceProvider { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public override string ToolTip()
    {
        return "Do not support this type of clipboard";
    }

    public override Task UploadProfile(IWebDav webdav, CancellationToken cancelToken)
    {
        return Task.CompletedTask;
    }

    protected override Task<bool> Same(Profile rhs, CancellationToken cancellationToken)
    {
        try
        {
            var _ = (TextProfile)rhs;
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    protected override MetaInfomation CreateMetaInformation()
    {
        throw new System.NotImplementedException();
    }
}