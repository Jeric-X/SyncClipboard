using SyncClipboard.Core.RemoteServer.Adapter;

namespace SyncClipboard.Core.RemoteServer.LogInHelper;

public interface ILoginHelper
{
    string TypeName { get; }
    string LoginPageName { get; }
}

public interface ILoginHelper<T> : ILoginHelper where T : IAdapterConfig<T>
{
}
