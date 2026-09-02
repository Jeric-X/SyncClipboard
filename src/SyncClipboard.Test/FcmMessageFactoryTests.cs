using FirebaseAdmin.Messaging;
using SyncClipboard.Server.Core.Services.Notifications.Fcm;

namespace SyncClipboard.Test;

[TestClass]
public class FcmMessageFactoryTests
{
    [TestMethod]
    public void CreateProfileChangedMessage_UsesCollapsedNormalPriorityHashOnlyPayload()
    {
        var message = FcmMessageFactory.CreateProfileChangedMessage(
            "push-token", "profile-hash");

#pragma warning disable CS0618 // S4/M7 protocol intentionally targets an FCM registration token.
        Assert.AreEqual("push-token", message.Token);
#pragma warning restore CS0618
        Assert.IsNull(message.Notification);
        Assert.HasCount(3, message.Data);
        Assert.AreEqual("1", message.Data["v"]);
        Assert.AreEqual("clipboard_changed", message.Data["type"]);
        Assert.AreEqual("profile-hash", message.Data["hash"]);
        Assert.IsFalse(message.Data.ContainsKey("text"));
        Assert.IsNotNull(message.Android);
        Assert.AreEqual("clipboard", message.Android.CollapseKey);
        Assert.AreEqual(Priority.Normal, message.Android.Priority);
    }
}
