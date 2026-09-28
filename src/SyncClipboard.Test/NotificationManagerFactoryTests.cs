using Moq;
using NativeNotification.Interface;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Test;

[TestClass]
public class NotificationManagerFactoryTests
{
    [TestMethod]
    public void Create_WhenInitializationSucceeds_PreservesNativeManager()
    {
        var nativeManager = Mock.Of<INotificationManager>();
        var logger = new Mock<ILogger>(MockBehavior.Strict);

        var manager = NotificationManagerFactory.Create(() => nativeManager, logger.Object);

        Assert.AreSame(nativeManager, manager);
        logger.VerifyNoOtherCalls();
    }

    [TestMethod]
    public void Create_WhenInitializationFails_LogsCauseAndKeepsNotificationCallsSafe()
    {
        var failure = new AggregateException(new InvalidOperationException("No path specified for UNIX transport"));
        var logger = new Mock<ILogger>();

        var manager = NotificationManagerFactory.Create(() => throw failure, logger.Object);

        logger.Verify(log => log.Write(nameof(NotificationManagerFactory),
            It.Is<string>(message => message.Contains(failure.ToString()) && message.Contains("disabled"))), Times.Once);
        Assert.IsFalse(manager.IsAppLaunchedByNotification);
        Assert.AreSame(manager.Shared, manager.Shared);
        var activated = false;
        manager.ActionActivated += _ => activated = true;
        var notification = manager.Create();
        notification.ContentAction = () => activated = true;
        notification.Title = "Clipboard synchronized";
        notification.Message = "Synchronization continues without desktop notifications.";
        notification.Show();
        Assert.IsFalse(notification.IsAlive);
        Assert.IsFalse(notification.Update());
        notification.Remove();
        manager.Shared.Show();
        manager.Show("Title", "Message");
        manager.Show("Title", "Message", []);

        foreach (var progress in new[] { manager.CreateProgress(), manager.CreateProgress(true) })
        {
            progress.ProgressValue = 0.5;
            progress.Show();
            Assert.IsFalse(progress.Update());
            progress.Remove();
        }

        manager.RomoveAllNotifications();
        Assert.IsEmpty(manager.GetAllNotifications());
        Assert.IsFalse(activated);
        logger.VerifyNoOtherCalls();
    }
}
