using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Moq;
using SyncClipboard.Core.Models;
using SyncClipboard.Desktop.ClipboardAva;
using SyncClipboard.Desktop.ClipboardAva.ClipboardWriter;
using SyncClipboard.Shared.Profiles;

namespace SyncClipboard.Test.Desktop;

[TestClass]
public class ClipboardBitmapLifetimeTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task PackageRetainsBitmapForBothAccessPathsAndDisposesItOnlyOnce()
    {
        using var bitmap = new TestBitmap();
        using var package = CreatePackage(bitmap);
        var duplicate = new DataTransferItem();
        duplicate.Set(DataFormat.Bitmap, bitmap);
        package.Data.Add(duplicate);

        Assert.Contains(DataFormat.Bitmap, package.Formats);
        Assert.AreSame(bitmap, package.Items[0].TryGetRaw(DataFormat.Bitmap));
        Assert.AreSame(bitmap, await ((IAsyncDataTransfer)package).Items[0].TryGetBitmapAsync());
        bitmap.Implementation.Verify(value => value.Dispose(), Times.Never);
        Assert.AreEqual(0, bitmap.DisposeCount);

        package.Dispose();
        package.Dispose();

        Assert.AreEqual(1, bitmap.DisposeCount);
        bitmap.Implementation.Verify(value => value.Dispose(), Times.Once);
    }

    [TestMethod]
    public async Task SuccessfulWriteRetainsBitmapUntilClipboardReleasesPackage()
    {
        using var bitmap = new TestBitmap();
        using var package = CreatePackage(bitmap);
        var clipboard = new Mock<IClipboard>();
        clipboard.Setup(value => value.SetDataAsync(package)).Returns(Task.CompletedTask);

        await new AvaloniaClipboardWriter(clipboard.Object).SetDataAsync(package, CancellationToken.None);

        clipboard.Verify(value => value.SetDataAsync(package), Times.Once);
        Assert.AreEqual(0, bitmap.DisposeCount);
        Assert.AreSame(bitmap, await ((IAsyncDataTransfer)package).TryGetBitmapAsync());
        ((IAsyncDataTransfer)package).Dispose();
        Assert.AreEqual(1, bitmap.DisposeCount);
    }

    [TestMethod]
    public async Task CancellingWaitDoesNotDisposeDataStillOwnedByClipboard()
    {
        using var bitmap = new TestBitmap();
        using var package = CreatePackage(bitmap);
        using var cancellation = new CancellationTokenSource();
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clipboard = new Mock<IClipboard>();
        clipboard.Setup(value => value.SetDataAsync(package)).Returns(operation.Task);

        var write = new AvaloniaClipboardWriter(clipboard.Object).SetDataAsync(package, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            write.WaitAsync(TimeSpan.FromSeconds(3), TestContext.CancellationTokenSource.Token));

        Assert.IsFalse(operation.Task.IsCompleted);
        Assert.AreEqual(0, bitmap.DisposeCount);
        bitmap.Implementation.Verify(value => value.Dispose(), Times.Never);
        operation.SetResult();
        ((IAsyncDataTransfer)package).Dispose();
        Assert.AreEqual(1, bitmap.DisposeCount);
    }

    [TestMethod]
    public void SynchronousPlatformFailureDoesNotDisposeDataAfterSubmission()
    {
        using var bitmap = new TestBitmap();
        using var package = CreatePackage(bitmap);
        var clipboard = new Mock<IClipboard>();
        var failure = new InvalidOperationException("平台调用失败");
        clipboard.Setup(value => value.SetDataAsync(package)).Throws(failure);

        var writer = new AvaloniaClipboardWriter(clipboard.Object);
        var actual = Assert.Throws<InvalidOperationException>(() => writer.SetDataAsync(package, CancellationToken.None));

        Assert.AreSame(failure, actual);
        Assert.AreEqual(0, bitmap.DisposeCount);
        clipboard.Verify(value => value.SetDataAsync(package), Times.Once());
        package.Dispose();
        Assert.AreEqual(1, bitmap.DisposeCount);
    }

    [TestMethod]
    public async Task FailedPreparationDisposesBitmapAlreadyAddedToPackage()
    {
        using var bitmap = new TestBitmap();
        var setter = new FailingSetter(bitmap);

        await Assert.ThrowsAsync<IOException>(() =>
            setter.SetLocalClipboard(new ClipboardMetaInfomation(), CancellationToken.None));

        Assert.AreEqual(1, bitmap.DisposeCount);
    }

    [TestMethod]
    public void AllNonBitmapResourcesAreOwnedWithoutRegistration()
    {
        var resource = new Mock<IDisposable>();
        var item = new DataTransferItem();
        item.Set(DataFormat.CreateInProcessFormat<IDisposable>("local-resource"), resource.Object);
        using var package = new AutoDisposeDataTransfer(new DataTransfer());
        package.Data.Add(item);

        package.Dispose();
        package.Dispose();

        resource.Verify(value => value.Dispose(), Times.Once());
    }

    [TestMethod]
    public void ResourcesAddedBeforeAndAfterWrappingAreBothDisposed()
    {
        var first = new Mock<IDisposable>();
        var second = new Mock<IDisposable>();
        var item = new DataTransferItem();
        item.Set(DataFormat.CreateInProcessFormat<IDisposable>("before-wrap"), first.Object);
        var data = new DataTransfer();
        data.Add(item);
        using var package = new AutoDisposeDataTransfer(data);
        item.Set(DataFormat.CreateInProcessFormat<IDisposable>("after-wrap"), second.Object);
        item.Set(DataFormat.CreateInProcessFormat<IDisposable>("shared-reference"), first.Object);
        var duplicate = new DataTransferItem();
        duplicate.Set(DataFormat.CreateInProcessFormat<IDisposable>("another-item"), first.Object);
        package.Data.Add(duplicate);

        package.Dispose();
        package.Dispose();

        first.Verify(value => value.Dispose(), Times.Once);
        second.Verify(value => value.Dispose(), Times.Once);
    }

    [TestMethod]
    public void DisposalContinuesWhenOneResourceThrows()
    {
        var first = new Mock<IDisposable>();
        var second = new Mock<IDisposable>();
        first.Setup(value => value.Dispose()).Throws(new IOException());
        var item = new DataTransferItem();
        item.Set(DataFormat.CreateInProcessFormat<IDisposable>("first-resource"), first.Object);
        item.Set(DataFormat.CreateInProcessFormat<IDisposable>("second-resource"), second.Object);
        using var package = new AutoDisposeDataTransfer(new DataTransfer());
        package.Data.Add(item);

        Assert.Throws<AggregateException>(() => package.Dispose());
        package.Dispose();

        first.Verify(value => value.Dispose(), Times.Once);
        second.Verify(value => value.Dispose(), Times.Once);
    }

    [TestMethod]
    public async Task FaultedPlatformOperationPropagatesExceptionAndStillOwnsItsData()
    {
        using var bitmap = new TestBitmap();
        using var package = CreatePackage(bitmap);
        var clipboard = new Mock<IClipboard>();
        var failure = new IOException("写入失败");
        clipboard.Setup(value => value.SetDataAsync(package)).Returns(Task.FromException(failure));

        var actual = await Assert.ThrowsAsync<IOException>(() =>
            new AvaloniaClipboardWriter(clipboard.Object).SetDataAsync(package, CancellationToken.None));

        Assert.AreSame(failure, actual);
        Assert.AreEqual(0, bitmap.DisposeCount);
        package.Dispose();
        Assert.AreEqual(1, bitmap.DisposeCount);
    }

    internal static AutoDisposeDataTransfer CreatePackage(Bitmap bitmap)
    {
        var item = new DataTransferItem();
        item.Set(DataFormat.Bitmap, bitmap);
        var data = new AutoDisposeDataTransfer(new DataTransfer());
        data.Data.Add(item);
        return data;
    }

    internal sealed class TestBitmap : Bitmap
    {
        public Mock<IBitmapImpl> Implementation { get; }
        public int DisposeCount { get; private set; }

        public TestBitmap() : this(new Mock<IBitmapImpl>()) { }

        private TestBitmap(Mock<IBitmapImpl> implementation) : base(implementation.Object)
        {
            Implementation = implementation;
        }

        public override void Dispose()
        {
            DisposeCount++;
            base.Dispose();
        }
    }

    private sealed class FailingSetter(Bitmap bitmap) : ClipboardSetterBase<TextProfile>
    {
        public override Task FillPackage(object package, ClipboardMetaInfomation metaInfomation)
        {
            var item = new DataTransferItem();
            item.Set(DataFormat.Bitmap, bitmap);
            ((DataTransfer)package).Add(item);
            throw new IOException("Image format preparation failed.");
        }
    }
}
