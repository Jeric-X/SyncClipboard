using Avalonia.Input;
using Avalonia.Input.Platform;
using Moq;
using SyncClipboard.Desktop.ClipboardAva;
using SyncClipboard.Desktop.ClipboardAva.ClipboardWriter;
using System.Runtime.CompilerServices;

namespace SyncClipboard.Test.Desktop;

[TestClass]
[DoNotParallelize]
public class AvaloniaClipboardWriterLifetimeTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ReplacementDisposesPreviousPackageOnlyAfterPlatformCompletes()
    {
        var resource = new DisposableProbe();
        using var previous = CreatePackage(resource);
        using var next = CreatePackage(new DisposableProbe());
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clipboard = new Mock<IClipboard>();
        clipboard.Setup(value => value.SetDataAsync(previous)).Returns(Task.CompletedTask);
        clipboard.Setup(value => value.SetDataAsync(next)).Returns(operation.Task);
        var writer = new AvaloniaClipboardWriter(clipboard.Object);
        await writer.SetDataAsync(previous, CancellationToken.None);

        var write = writer.SetDataAsync(next, CancellationToken.None);
        Assert.AreEqual(0, resource.DisposeCount);
        operation.SetResult();
        await write;

        Assert.AreEqual(1, resource.DisposeCount);
        Assert.HasCount(1, next.Items);
    }

    [TestMethod]
    public async Task TextReplacementAlsoReleasesPreviousPackage()
    {
        var resource = new DisposableProbe();
        using var previous = CreatePackage(resource);
        var clipboard = new Mock<IClipboard>();
        clipboard.Setup(value => value.SetDataAsync(It.IsAny<IAsyncDataTransfer>())).Returns(Task.CompletedTask);
        var writer = new AvaloniaClipboardWriter(clipboard.Object);
        await writer.SetDataAsync(previous, CancellationToken.None);

        await writer.SetTextAsync("新文本", CancellationToken.None);

        Assert.AreEqual(1, resource.DisposeCount);
        var written = (IAsyncDataTransfer)clipboard.Invocations[^1].Arguments[0];
        Assert.AreEqual("新文本", await written.TryGetTextAsync());
        written.Dispose();
    }

    [TestMethod]
    public async Task RewritingTheSamePackageDoesNotDisposeIt()
    {
        var resource = new DisposableProbe();
        using var package = CreatePackage(resource);
        var clipboard = new Mock<IClipboard>();
        clipboard.Setup(value => value.SetDataAsync(package)).Returns(Task.CompletedTask);
        var writer = new AvaloniaClipboardWriter(clipboard.Object);

        await writer.SetDataAsync(package, CancellationToken.None);
        await writer.SetDataAsync(package, CancellationToken.None);

        Assert.AreEqual(0, resource.DisposeCount);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancelledWaitPreservesPreviousRecordAndDoesNotBlockNextWrite(bool completeBeforeNextWrite)
    {
        var previousResource = new DisposableProbe();
        var cancelledResource = new DisposableProbe();
        var nextResource = new DisposableProbe();
        using var previous = CreatePackage(previousResource);
        using var cancelled = CreatePackage(cancelledResource);
        using var next = CreatePackage(nextResource);
        using var last = CreatePackage(new DisposableProbe());
        using var cancellation = new CancellationTokenSource();
        var operation = new TaskCompletionSource();
        var clipboard = new Mock<IClipboard>();
        clipboard.Setup(value => value.SetDataAsync(It.IsAny<IAsyncDataTransfer>())).Returns(Task.CompletedTask);
        clipboard.Setup(value => value.SetDataAsync(cancelled)).Returns(operation.Task);
        var writer = new AvaloniaClipboardWriter(clipboard.Object);
        await writer.SetDataAsync(previous, CancellationToken.None);
        var write = writer.SetDataAsync(cancelled, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => write);

        Assert.AreEqual(0, previousResource.DisposeCount);
        Assert.AreEqual(0, cancelledResource.DisposeCount);
        if (completeBeforeNextWrite)
            operation.SetResult();

        // 后续写入无需等待被取消的操作，仍释放上一次成功记录的旧包。
        await writer.SetDataAsync(next, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        Assert.AreEqual(completeBeforeNextWrite, operation.Task.IsCompleted);
        Assert.AreEqual(1, previousResource.DisposeCount);
        Assert.AreEqual(0, cancelledResource.DisposeCount);

        if (!completeBeforeNextWrite)
            operation.SetResult();
        await writer.SetDataAsync(last, CancellationToken.None);

        // 被取消的操作后来完成也不会替换记录，下一次释放的仍是 next。
        Assert.AreEqual(1, nextResource.DisposeCount);
        Assert.AreEqual(0, cancelledResource.DisposeCount);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FailedReplacementPreservesPreviousPackageUntilNextSuccessfulWrite(bool synchronousFailure)
    {
        var previousResource = new DisposableProbe();
        var failedResource = new DisposableProbe();
        using var previous = CreatePackage(previousResource);
        using var failed = CreatePackage(failedResource);
        using var next = CreatePackage(new DisposableProbe());
        var clipboard = new Mock<IClipboard>();
        clipboard.Setup(value => value.SetDataAsync(previous)).Returns(Task.CompletedTask);
        if (synchronousFailure)
            clipboard.Setup(value => value.SetDataAsync(failed)).Throws(new IOException("写入失败"));
        else
            clipboard.Setup(value => value.SetDataAsync(failed)).Returns(Task.FromException(new IOException("写入失败")));
        clipboard.Setup(value => value.SetDataAsync(next)).Returns(Task.CompletedTask);
        var writer = new AvaloniaClipboardWriter(clipboard.Object);
        await writer.SetDataAsync(previous, CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(() => writer.SetDataAsync(failed, CancellationToken.None));
        Assert.AreEqual(0, previousResource.DisposeCount);
        Assert.AreEqual(0, failedResource.DisposeCount);
        await writer.SetDataAsync(next, CancellationToken.None);

        Assert.AreEqual(1, previousResource.DisposeCount);
        // 平台可能在抛异常前已持有失败包，不能仅凭异常主动释放它。
        Assert.AreEqual(0, failedResource.DisposeCount);
    }

    [TestMethod]
    public void WeakTrackingAllowsFinalizerToReleaseUnreferencedPackage()
    {
        var resource = new DisposableProbe();
        var clipboard = new Mock<IClipboard>();
        clipboard.Setup(value => value.SetDataAsync(It.IsAny<IAsyncDataTransfer>())).Returns(Task.CompletedTask);
        var writer = new AvaloniaClipboardWriter(clipboard.Object);
        var reference = WriteUnreferencedPackage(writer, resource);
        // 模拟平台已不再持有包；Moq 的调用记录也不能保留强引用。
        clipboard.Invocations.Clear();

        CollectFinalizers();

        Assert.IsFalse(reference.TryGetTarget(out _));
        Assert.AreEqual(1, resource.DisposeCount);
        GC.KeepAlive(writer);
    }

    [TestMethod]
    public void FinalizerContinuesAfterFailureAndDisposesSharedResourcesOnlyOnce()
    {
        var failing = new DisposableProbe(throws: true);
        var normal = new DisposableProbe();
        var reference = CreateUnreferencedPackage(failing, normal, failing);

        CollectFinalizers();

        Assert.IsFalse(reference.TryGetTarget(out _));
        Assert.AreEqual(1, failing.DisposeCount);
        Assert.AreEqual(1, normal.DisposeCount);
    }

    [TestMethod]
    public void ExplicitDisposalDoesNotRepeatDuringFinalization()
    {
        var resource = new DisposableProbe();
        var reference = DisposeUnreferencedPackage(resource);

        CollectFinalizers();

        Assert.IsFalse(reference.TryGetTarget(out _));
        Assert.AreEqual(1, resource.DisposeCount);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<AutoDisposeDataTransfer> WriteUnreferencedPackage(
        AvaloniaClipboardWriter writer, DisposableProbe resource)
    {
        var package = CreatePackage(resource);
        writer.SetDataAsync(package, CancellationToken.None).GetAwaiter().GetResult();
        return new WeakReference<AutoDisposeDataTransfer>(package);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<AutoDisposeDataTransfer> CreateUnreferencedPackage(params IDisposable[] resources) =>
        new(CreatePackage(resources));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<AutoDisposeDataTransfer> DisposeUnreferencedPackage(IDisposable resource)
    {
        var package = CreatePackage(resource);
        package.Dispose();
        return new WeakReference<AutoDisposeDataTransfer>(package);
    }

    private static void CollectFinalizers()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static AutoDisposeDataTransfer CreatePackage(params IDisposable[] resources)
    {
        var data = new DataTransfer();
        foreach (var resource in resources)
        {
            var item = new DataTransferItem();
            item.Set(DataFormat.CreateInProcessFormat<IDisposable>("resource"), resource);
            data.Add(item);
        }
        return new AutoDisposeDataTransfer(data);
    }

    private sealed class DisposableProbe(bool throws = false) : IDisposable
    {
        private int _disposeCount;
        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCount);
            if (throws) throw new IOException("释放失败");
        }
    }
}
