using SyncClipboard.Core.Interfaces;

namespace SyncClipboard.Core.Clipboard;

public class LocalClipboardSetter(IServiceProvider serviceProvider, IThreadDispatcher dispather)
{
    public async Task Set(Profile profile, CancellationToken ctk, bool mutex = true)
    {
        var profileType = profile.GetType();
        var setterInterface = typeof(IClipboardSetter<>).MakeGenericType(profileType);
        var setter = serviceProvider.GetService(setterInterface) as IClipboardSetter;

        ArgumentNullException.ThrowIfNull(setter, $"No IClipboardSetter service is registered for type {profileType.Name}");

        if (mutex)
        {
            await LocalClipboard.Semaphore.WaitAsync(ctk);
        }

        try
        {
            await dispather.RunOnMainThreadAsync(() => setter.SetLocalClipboard(profile.MetaInfomation, ctk));

        }
        finally
        {
            if (mutex)
            {
                LocalClipboard.Semaphore.Release();
            }
        }
    }
}
