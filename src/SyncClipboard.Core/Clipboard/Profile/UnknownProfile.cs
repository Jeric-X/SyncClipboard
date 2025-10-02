using SyncClipboard.Abstract;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Clipboard;

public class UnknownProfile : Profile
{
    public override ProfileType Type => ProfileType.Unknown;

    protected override IClipboardSetter<Profile> ClipboardSetter => throw new NotImplementedException();

    protected override bool Same(Profile rhs)
    {
        return rhs is UnknownProfile;
    }

    protected override ClipboardMetaInfomation CreateMetaInformation()
    {
        throw new NotImplementedException();
    }

    public override bool IsAvailableFromRemote() => false;

    public override string ShowcaseText()
    {
        return "Do not support this type of clipboard";
    }

    public override HistoryRecord CreateHistoryRecord()
    {
        throw new NotImplementedException();
    }
}