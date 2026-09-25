using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Shared.Profiles;

namespace SyncClipboard.Test;

[TestClass]
public class ClipboardChangingListenerBaseTest
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task NotificationDoesNotWaitForBusinessLayerClipboardMutex()
    {
        var factory = new TestClipboardFactory();
        using var listener = new TestClipboardListener(factory);
        TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.Changed += (_, _) => changed.TrySetResult();

        await LocalClipboard.Semaphore.WaitAsync(TestContext.CancellationTokenSource.Token);
        try
        {
            listener.Trigger();
            await changed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.CancellationTokenSource.Token);

            Assert.AreEqual(1, factory.GetMetaInfomationCallCount, "Native clipboard synchronization belongs in the platform factory, not the business-layer mutex.");
        }
        finally
        {
            LocalClipboard.Semaphore.Release();
        }
    }

    [TestMethod]
    public async Task RapidNotificationsAreNotCoalesced()
    {
        var factory = new TestClipboardFactory();
        using var listener = new TestClipboardListener(factory);
        TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int changedCount = 0;
        listener.Changed += (_, _) =>
        {
            if (Interlocked.Increment(ref changedCount) == 3)
            {
                changed.TrySetResult();
            }
        };

        listener.Trigger();
        listener.Trigger();
        listener.Trigger();

        await changed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.CancellationTokenSource.Token);

        Assert.AreEqual(3, factory.GetMetaInfomationCallCount);
        Assert.AreEqual(3, changedCount);
    }

    [TestMethod]
    public async Task RootDirectoryProfile_IsDeliveredUnchangedToSubscribers()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var profile = new GroupProfile([root]);
        var factory = new TestClipboardFactory { ProfileToReturn = profile };
        using var listener = new TestClipboardListener(factory);
        var changed = new TaskCompletionSource<Profile>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.Changed += (_, received) => changed.TrySetResult(received);

        listener.Trigger();

        var received = await changed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.CancellationTokenSource.Token);
        Assert.AreSame(profile, received);
        Assert.IsTrue(((GroupProfile)received).ContainsRootDirectory);
    }

    private sealed class TestClipboardListener(IClipboardFactory clipboardFactory) : ClipboardChangingListenerBase
    {
        private MetaChanged? _action;

        protected override IClipboardFactory ClipboardFactory { get; } = clipboardFactory;

        protected override void RegistSystemEvent(MetaChanged action) => _action = action;

        protected override void UnRegistSystemEvent(MetaChanged action) => _action = null;

        public void Trigger() => _action?.Invoke(null);
    }

    private sealed class TestClipboardFactory : IClipboardFactory
    {
        private int _getMetaInfomationCallCount;

        public int GetMetaInfomationCallCount => Volatile.Read(ref _getMetaInfomationCallCount);
        public Profile? ProfileToReturn { get; init; }

        public Task<ClipboardMetaInfomation> GetMetaInfomation(CancellationToken ctk)
        {
            ctk.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _getMetaInfomationCallCount);
            return Task.FromResult(new ClipboardMetaInfomation { Text = "test" });
        }

        public Task<Profile> CreateProfileFromMeta(ClipboardMetaInfomation metaInfomation, CancellationToken ctk)
        {
            ctk.ThrowIfCancellationRequested();
            return Task.FromResult(ProfileToReturn ?? new TextProfile(metaInfomation.Text ?? string.Empty));
        }

        public Task<Profile> CreateProfileFromMeta(ClipboardMetaInfomation metaInfomation, bool contentControl, CancellationToken ctk)
            => CreateProfileFromMeta(metaInfomation, ctk);

        public Task<Profile> CreateProfileFromLocal(CancellationToken ctk)
            => CreateProfileFromMeta(new ClipboardMetaInfomation { Text = "test" }, ctk);

        public void SetClipboardOwner(ClipboardMetaInfomation meta) { }
    }
}
