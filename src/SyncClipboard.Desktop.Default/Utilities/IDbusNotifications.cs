using System.Runtime.CompilerServices;
using Tmds.DBus;

[assembly: InternalsVisibleTo(Connection.DynamicAssemblyName)]
namespace SyncClipboard.Desktop.Default.Utilities;

[DBusInterface("org.freedesktop.Notifications")]
internal interface IDbusNotifications : IDBusObject
{
    Task<string[]> GetCapabilitiesAsync();
    Task CloseNotificationAsync(uint id);
    Task<uint> NotifyAsync(string appName, uint replacesId, string appIcon, string summary, string body, string[] actions, IDictionary<string, object> hints, int expireTimeout);
    Task<(string name, string vendor, string version, string specVersion)> GetServerInformationAsync();
    Task<IDisposable> WatchNotificationClosedAsync(Action<(uint id, uint reason)> handler, Action<Exception>? onError = null);
    Task<IDisposable> WatchActionInvokedAsync(Action<(uint id, string actionKey)> handler, Action<Exception>? onError = null);
}