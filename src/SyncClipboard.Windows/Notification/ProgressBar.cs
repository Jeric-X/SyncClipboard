using Microsoft.Toolkit.Uwp.Notifications;
using SyncClipboard.Abstract.Notification;
using Windows.UI.Notifications;

namespace SyncClipboard.Windows.Notification
{
    public class ProgressBar(string title, ToastNotifier notifer, CallbackHandler<string> callbackHandler)
        : ToastSession(title, notifer, callbackHandler), IProgressBar
    {
        private readonly ToastNotifier _notifer = notifer;

        public string? ProgressTitle { get; set; }
        public double? ProgressValue { get; set; }
        public bool IsIndeterminate { get; set; } = false;
        public string? ProgressValueTip { get; set; }
        public string ProgressStatus { get; set; } = "Status";
        private uint _equenceNumber = 0;

        private const string PROGRESS_BINDING_TITLE = "PROGRESS_BINDING_TITLE";
        private const string PROGRESS_BINDING_VALUE = "PROGRESS_BINDING_VALUE";
        private const string PROGRESS_BINDING_VALUE_TIP = "PROGRESS_BINDING_VALUE_TIP";
        private const string PROGRESS_BINDING_STATUS = "PROGRESS_BINDING_STATUS";

        protected override ToastContentBuilder GetBuilder()
        {
            var builder = base.GetBuilder();
            builder.AddVisualChild(
                new AdaptiveProgressBar()
                {
                    Title = new BindableString(PROGRESS_BINDING_TITLE),
                    Value = IsIndeterminate ? AdaptiveProgressBarValue.Indeterminate : new BindableProgressBarValue(PROGRESS_BINDING_VALUE),
                    ValueStringOverride = new BindableString(PROGRESS_BINDING_VALUE_TIP),
                    Status = new BindableString(PROGRESS_BINDING_STATUS)
                }
            );
            return builder;
        }

        protected override ToastNotification GetToast(ToastContentBuilder builder)
        {
            var toast = base.GetToast(builder);
            SetBingData(toast.Data);
            return toast;
        }

        private NotificationData SetBingData(NotificationData? data = null)
        {
            data ??= new();
            data.Values[PROGRESS_BINDING_TITLE] = ProgressTitle;
            data.Values[PROGRESS_BINDING_VALUE] = ProgressValue.ToString();
            data.Values[PROGRESS_BINDING_VALUE_TIP] = ProgressValueTip;
            data.Values[PROGRESS_BINDING_STATUS] = ProgressStatus;
            data.SequenceNumber = _equenceNumber++;
            return data;
        }

        public NotificationUpdateResult Upadate(double value, string? valueTip = null, string? status = null)
        {
            ProgressValue = value;
            ProgressValueTip = valueTip ?? ProgressValueTip;
            ProgressStatus = status ?? ProgressStatus;
            return _notifer.Update(SetBingData(), Tag, Group);
        }

        public bool Upadate()
        {
            return _notifer.Update(SetBingData(), Tag, Group) is NotificationUpdateResult.Succeeded;
        }

        public void ForceUpdate(double value, string? valueTip = null, string? status = null)
        {
            if (Upadate(value, valueTip, status) is NotificationUpdateResult.NotificationNotFound)
            {
                Show();
            }
        }
    }
}
