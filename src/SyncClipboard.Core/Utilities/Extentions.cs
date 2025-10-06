using Microsoft.Extensions.DependencyInjection;
using NativeNotification.Interface;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.RemoteServer.Adapter;
using SyncClipboard.Core.RemoteServer.LogInHelper;
using System.Reflection;

namespace SyncClipboard.Core.Utilities;

public static class Extentions
{
    public static LocaleString<T> Match<T>(this IEnumerable<LocaleString<T>> localeStrings, T obj)
    {
        if (!localeStrings.Any())
        {
            throw new ArgumentNullException(nameof(localeStrings));
        }
        return localeStrings.FirstOrDefault(x => EqualityComparer<T>.Default.Equals(x.Key, obj)) ?? localeStrings.First();
    }

    public static INotification ShowText(this INotificationManager notificationManager, string title, string message, IEnumerable<ActionButton>? buttons = null)
    {
        return notificationManager.Show(title, message, buttons);
    }

    public static void SharedQuickMessage(this INotificationManager notificationManager, string title, string message, IEnumerable<ActionButton>? buttons = null)
    {
        notificationManager.Shared.Remove();
        notificationManager.Shared.Title = title;
        notificationManager.Shared.Message = message;
        notificationManager.Shared.Buttons = buttons?.ToList() ?? [];
        notificationManager.Shared.Show(new NotificationDeliverOption { Duration = TimeSpan.FromSeconds(2) });
    }

    public static void AddServerAdapter<TConfig, TAdapter>(this IServiceCollection services)
        where TConfig : IAdapterConfig<TConfig>
        where TAdapter : class, IStorageOnlyServerAdapter<TConfig>
    {
        var typeNameProperty = typeof(IAdapterConfig<TConfig>).GetProperty("TypeName", BindingFlags.Static | BindingFlags.Public);
        var key = (string)typeNameProperty!.GetValue(null)!;
        services.AddKeyedTransient<IStorageOnlyServerAdapter, TAdapter>(key);
    }

    public static void AddLogInHelper<TConfig, THelper>(this IServiceCollection services)
        where TConfig : IAdapterConfig<TConfig>
        where THelper : class, ILoginHelper<TConfig>
    {
        services.AddTransient<ILoginHelper, THelper>();
    }
}