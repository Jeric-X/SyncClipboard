using NativeNotification.Common;
using NativeNotification.Interface;

namespace SyncClipboard.Core.Utilities;

internal sealed class NoOpNotificationManager : NotificationManagerBase
{
    public override INotification Create() => new NoOpNotification();

    public override IProgressNotification CreateProgress(bool suppressNotSupportedException) => new NoOpNotification();

    public override void RomoveAllNotifications()
    {
    }

    private sealed class NoOpNotification : IProgressNotification
    {
        public string? Title { get; set; }
        public string? Message { get; set; }
        public Uri? Image { get; set; }
        public List<ActionButton> Buttons { get; set; } = [];
        public Action? ContentAction { get; set; }
        public string NotificationId { get; } = Guid.NewGuid().ToString();
        public bool IsAlive => false;
        public bool IsCreatedByCurrentProcess => true;
        public string? ProgressTitle { get; set; }
        public double? ProgressValue { get; set; }
        public string? ProgressValueTip { get; set; }
        public bool IsIndeterminate { get; set; }

        public void Show(NotificationDeliverOption? config = null)
        {
        }

        public bool Update() => false;

        public void Remove()
        {
        }
    }
}
