using Microsoft.AspNetCore.SignalR;
using Moq;
using SyncClipboard.Server.Core.Hubs;
using SyncClipboard.Server.Core.Services.Notifications;
using SyncClipboard.Shared;
using SyncClipboard.Shared.Profiles;

namespace SyncClipboard.Test;

[TestClass]
public class SignalRProfileChangeNotifierTests
{
    [TestMethod]
    public async Task NotifyProfileChanged_BroadcastsProfileToAllSignalRClients()
    {
        var profile = new ProfileDto
        {
            Type = ProfileType.Text,
            Hash = "profile-hash",
            Text = "clipboard"
        };
        var client = new Mock<ISyncClipboardClient>();
        var clients = new Mock<IHubClients<ISyncClipboardClient>>();
        clients.SetupGet(value => value.All).Returns(client.Object);
        var hubContext = new Mock<IHubContext<SyncClipboardHub, ISyncClipboardClient>>();
        hubContext.SetupGet(value => value.Clients).Returns(clients.Object);
        var notifier = new SignalRProfileChangeNotifier(hubContext.Object);

        await notifier.NotifyProfileChanged(
            new ProfileChangeNotification(profile, "origin-device-id"),
            CancellationToken.None);

        client.Verify(value => value.RemoteProfileChanged(profile), Times.Once);
    }
}
