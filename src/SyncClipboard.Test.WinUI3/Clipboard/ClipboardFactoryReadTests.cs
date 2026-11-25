using Microsoft.Extensions.DependencyInjection;
using Moq;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.WinUI3.ClipboardWinUI;
using System.Runtime.InteropServices;

namespace SyncClipboard.Test.WinUI3.Clipboard;

[TestClass]
public class ClipboardFactoryReadTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task RpcFailureWithNewNotification_SkipsOldRead()
    {
        var logger = new Mock<ILogger>(MockBehavior.Strict);
        using var services = new ServiceCollection()
            .AddSingleton(logger.Object)
            .AddSingleton(Mock.Of<IThreadDispatcher>())
            .BuildServiceProvider();
        var factory = new ClipboardFactory(services);
        var changes = new ClipboardChangeTokenSource();
        var changeToken = changes.NotifyChange();
        int attempts = 0;
        var shouldContinue = await factory.ReadFormatWithRetryAsync((_, _, _) =>
        {
            attempts++;
            changes.NotifyChange();
            throw new COMException("Source unavailable", unchecked((int)0x800706BA));
        }, null!, new ClipboardMetaInfomation(), changeToken, TestContext.CancellationTokenSource.Token);

        Assert.IsFalse(shouldContinue);
        Assert.AreEqual(1, attempts);
        logger.VerifyNoOtherCalls();
    }
}
