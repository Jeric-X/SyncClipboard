using SyncClipboard.Abstract;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Clipboard;

public class UnknownProfile : Profile
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
        return Task.FromResult(rhs is UnknownProfile);
    }

    protected override ClipboardMetaInfomation CreateMetaInformation()
    {
        throw new System.NotImplementedException();
    }

    public override ValueTask<bool> IsAvailableFromRemote(CancellationToken _) => ValueTask.FromResult(false);
}