using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SyncClipboard.Server.Core.Models;
using SyncClipboard.Server.Core.Services.Notifications.Fcm;

namespace SyncClipboard.Test;

[TestClass]
public class FirebaseAdminFcmPushClientTests
{
    [TestMethod]
    public async Task DisabledConfiguration_RemainsUnavailableWithoutCredentials()
    {
        using var client = new FirebaseAdminFcmPushClient(
            Options.Create(new AppSettings { EnableFcmPush = false }),
            NullLogger<FirebaseAdminFcmPushClient>.Instance);

        Assert.IsFalse(client.IsAvailable);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            client.SendProfileChangedAsync(
                "push-token", "profile-hash", CancellationToken.None));
    }
}
