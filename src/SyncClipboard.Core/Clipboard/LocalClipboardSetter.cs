using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Clipboard;

public class LocalClipboardSetter(IServiceProvider serviceProvider, IThreadDispatcher dispather, IProfileEnv profileEnv)
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
            var localInfo = await profile.Localize(profileEnv.GetPersistentDir(), false, ctk);
            await dispather.RunOnMainThreadAsync(() => setter.SetLocalClipboard(localInfo.GetMetaInfomation(), ctk));
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
