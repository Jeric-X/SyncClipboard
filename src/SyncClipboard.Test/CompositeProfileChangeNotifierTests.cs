using Moq;
using SyncClipboard.Server.Core.Services.Notifications;
using SyncClipboard.Shared;

namespace SyncClipboard.Test;

[TestClass]
public class CompositeProfileChangeNotifierTests
{
    [TestMethod]
    public async Task NotifyProfileChanged_InvokesEveryProvider()
    {
        var signalR = new Mock<IProfileChangeNotifier>();
        var fcm = new Mock<IProfileChangeNotifier>();
        var notifier = new CompositeProfileChangeNotifier([signalR.Object, fcm.Object]);
        var profile = new ProfileDto { Hash = "profile-hash" };

        await notifier.NotifyProfileChanged(profile, CancellationToken.None);

        signalR.Verify(value => value.NotifyProfileChanged(profile, CancellationToken.None), Times.Once);
        fcm.Verify(value => value.NotifyProfileChanged(profile, CancellationToken.None), Times.Once);
    }
}
