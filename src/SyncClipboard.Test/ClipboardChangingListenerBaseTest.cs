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
    public async Task RapidNotificationsAreDeferredAndCoalesced()
    {
        var factory = new TestClipboardFactory();
        using var listener = new TestClipboardListener(factory);
        TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int changedCount = 0;
        listener.Changed += (_, _) =>
        {
            Interlocked.Increment(ref changedCount);
            changed.TrySetResult();
        };

        listener.Trigger();
        listener.Trigger();
        listener.Trigger();

        Assert.AreEqual(0, factory.GetMetaInfomationCallCount, "Clipboard reading must not run inline in the native change callback.");

        await changed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.CancellationTokenSource.Token);
        await Task.Delay(100, TestContext.CancellationTokenSource.Token);

        Assert.AreEqual(1, factory.GetMetaInfomationCallCount);
        Assert.AreEqual(1, changedCount);
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

        public Task<ClipboardMetaInfomation> GetMetaInfomation(CancellationToken ctk)
        {
            ctk.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _getMetaInfomationCallCount);
            return Task.FromResult(new ClipboardMetaInfomation { Text = "test" });
        }

        public Task<Profile> CreateProfileFromMeta(ClipboardMetaInfomation metaInfomation, CancellationToken ctk)
        {
            ctk.ThrowIfCancellationRequested();
            return Task.FromResult<Profile>(new TextProfile(metaInfomation.Text ?? string.Empty));
        }

        public Task<Profile> CreateProfileFromMeta(ClipboardMetaInfomation metaInfomation, bool contentControl, CancellationToken ctk)
            => CreateProfileFromMeta(metaInfomation, ctk);

        public Task<Profile> CreateProfileFromLocal(CancellationToken ctk)
            => CreateProfileFromMeta(new ClipboardMetaInfomation { Text = "test" }, ctk);

        public void SetClipboardOwner(ClipboardMetaInfomation meta) { }
    }
}
