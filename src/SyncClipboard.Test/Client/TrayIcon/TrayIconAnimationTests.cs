using SyncClipboard.Core.AbstractClasses;

namespace SyncClipboard.Test.Client.TrayIcon;

[TestClass]
public class TrayIconAnimationTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void StopWithPendingTickRestoresCurrentStaticIcon(bool upload)
    {
        var tray = new TestTrayIcon();
        if (upload)
        {
            tray.ShowUploadAnimation();
        }
        else
        {
            tray.ShowDownloadAnimation();
        }
        tray.QueueTick();
        tray.SetStatusString("Sync", "Failed", error: true);
        tray.SetActiveStatus(false);
        tray.Drain();
        Assert.AreEqual(upload ? "upload2" : "download2", tray.Icon);

        tray.StopAnimation();
        tray.QueueTick();
        tray.Drain();
        Assert.AreEqual("error-inactive", tray.Icon);
    }

    [TestMethod]
    public void StopThenStartBeforeDispatchPlaysNewAnimation()
    {
        var tray = new TestTrayIcon();
        tray.ShowUploadAnimation();
        tray.StopAnimation();
        tray.ShowDownloadAnimation();
        tray.Drain();
        Assert.AreEqual("download1", tray.Icon);

        tray.QueueTick();
        tray.Drain();
        Assert.AreEqual("download2", tray.Icon);
    }

    private sealed class TestTrayIcon : TrayIconBase<string>
    {
        private readonly Queue<Action> _queue = new();
        public string? Icon { get; private set; }
        public void QueueTick() => _queue.Enqueue(ShowNextFrame);
        public void Drain()
        {
            while (_queue.TryDequeue(out var action))
            {
                action();
            }
        }

        protected override string DefaultIcon => "default";
        protected override string DefaultInactiveIcon => "inactive";
        protected override string ErrorIcon => "error";
        protected override string ErrorInactiveIcon => "error-inactive";
        protected override int MaxToolTipLenth => 1024;
        protected override IEnumerable<string> UploadIcons() => ["upload1", "upload2"];
        protected override IEnumerable<string> DownloadIcons() => ["download1", "download2"];
        protected override void PostToUIThread(Action action) => _queue.Enqueue(action);
        protected override void SetIcon(string icon) => Icon = icon;
        protected override void SetToolTip(string text) { }
        protected override void StartAnimationTimer() { }
        protected override void StopAnimationTimer() { }
        public override void Create() { }
    }
}
