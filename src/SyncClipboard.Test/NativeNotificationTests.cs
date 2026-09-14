using Microsoft.Extensions.DependencyInjection;
using Moq;
using NativeNotification.Common;
using NativeNotification.Interface;
using SyncClipboard.Core;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Test;

[TestClass]
[TestCategory("NonUI")]
public class NativeNotificationTests
{
    private static readonly string[] ButtonLabels = ["复制", "无操作", "打开"];
    private static readonly string[] CallbackOrder = ["open", "copy"];

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ShowText_PreservesPayloadAndOptionalButtonCallbacks(bool includeButtons)
    {
        var notification = CreateNotification();
        var manager = CreateManager(notification.Object);
        var callbackCount = 0;
        var button = new ActionButton("重试", () => callbackCount++);
        var buttons = includeButtons ? new[] { button } : null;
        notification.Setup(x => x.Show(null)).Callback(() =>
        {
            Assert.AreEqual("同步结果", notification.Object.Title);
            Assert.AreEqual("文件 ✓\n第二行", notification.Object.Message);
            Assert.HasCount(includeButtons ? 1 : 0, notification.Object.Buttons);
        });

        var returned = manager.Object.ShowText("同步结果", "文件 ✓\n第二行", buttons);

        Assert.AreSame(notification.Object, returned);
        Assert.AreEqual(0, callbackCount);
        if (includeButtons)
        {
            Assert.AreSame(button, returned.Buttons.Single());
            returned.Buttons.Single().Callback!();
            Assert.AreEqual(1, callbackCount);
        }
        manager.Verify(x => x.Create(), Times.Once());
        notification.Verify(x => x.Show(null), Times.Once());
    }

    [TestMethod]
    public void SharedQuickMessage_ReusesNotificationAndClearsPreviousButtons()
    {
        var notification = CreateNotification();
        var manager = CreateManager(notification.Object);
        var callbackCount = 0;
        var button = new ActionButton("取消", () => callbackCount++);
        var removed = 0;
        var shown = 0;
        notification.Setup(x => x.Remove()).Callback(() => removed++);
        notification.Setup(x => x.Show(It.IsAny<NotificationDeliverOption>())).Callback((NotificationDeliverOption option) =>
        {
            shown++;
            Assert.AreEqual(shown, removed, "Remove must precede each replacement notification.");
            Assert.AreEqual(TimeSpan.FromSeconds(2), option.Duration);
            Assert.IsNull(option.ExpirationTime);
            Assert.IsFalse(option.Silent);
            Assert.AreEqual(shown == 1 ? "首次" : "再次", notification.Object.Title);
            Assert.AreEqual(shown == 1 ? "正在同步" : "已完成", notification.Object.Message);
            Assert.AreSequenceEqual(shown == 1 ? new[] { button } : [], notification.Object.Buttons);
        });

        manager.Object.SharedQuickMessage("首次", "正在同步", [button]);
        manager.Object.SharedQuickMessage("再次", "已完成");

        Assert.AreEqual(2, shown);
        Assert.AreEqual(0, callbackCount);
        manager.Verify(x => x.Create(), Times.Once());
        notification.Verify(x => x.Remove(), Times.Exactly(2));
    }

    [TestMethod]
    public void ActionButtons_PreserveOrderAndInvokeOnlyTheirOwnCallbacks()
    {
        List<string> calls = [];
        MenuItem[] items =
        [
            new(null, () => calls.Add("excluded")),
            new("复制", () => calls.Add("copy")),
            new("无操作"),
            new("打开", () => calls.Add("open"))
        ];

        var buttons = ProfileActionBuilder.ToActionButtons(items);

        Assert.AreSequenceEqual(ButtonLabels, buttons.Select(x => x.Text).ToArray());
        Assert.HasCount(buttons.Count, buttons.Select(x => x.ActionId).Distinct());
        Assert.IsTrue(buttons.All(x => !string.IsNullOrEmpty(x.ActionId)));
        Assert.IsEmpty(calls);
        buttons[2].Callback!();
        buttons[1].Callback!();
        buttons[0].Callback!();
        Assert.AreSequenceEqual(CallbackOrder, calls);
    }

    [TestMethod]
    public void ProfileNotificationRegistration_UsesOneNotificationFromInjectedManager()
    {
        var notification = CreateNotification();
        var manager = new Mock<INotificationManager>(MockBehavior.Strict);
        manager.Setup(x => x.Create()).Returns(notification.Object);
        var collection = new ServiceCollection();
        AppCore.ConfigCommonService(collection);
        // Override before resolving any services; the real platform factory is never invoked.
        collection.AddSingleton(manager.Object);
        using var services = collection.BuildServiceProvider();

        var first = services.GetRequiredKeyedService<INotification>("ProfileNotification");
        var second = services.GetRequiredKeyedService<INotification>("ProfileNotification");

        Assert.AreSame(notification.Object, first);
        Assert.AreSame(first, second);
        manager.Verify(x => x.Create(), Times.Once());
        manager.VerifyNoOtherCalls();
        notification.VerifyNoOtherCalls();
    }

    private static Mock<INotification> CreateNotification()
    {
        var notification = new Mock<INotification>(MockBehavior.Strict);
        notification.SetupAllProperties();
        notification.Object.Buttons = [];
        notification.Invocations.Clear();
        return notification;
    }

    private static Mock<NotificationManagerBase> CreateManager(INotification notification)
    {
        // The common base only owns managed session dictionaries. All platform operations use mocks.
        var manager = new Mock<NotificationManagerBase> { CallBase = true };
        manager.Setup(x => x.Create()).Returns(notification);
        return manager;
    }
}
