using System;
using Microsoft.Toolkit.Uwp.Notifications;
using Windows.UI.Notifications;
#nullable enable

namespace SyncClipboard.Utility.Notification
{
    public class ProgressBar : ToastSession
    {
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
            return Toast.Notifer.Update(SetBingData(), Tag, Group);
        }

        public NotificationUpdateResult Upadate()
        {
            return Toast.Notifer.Update(SetBingData(), Tag, Group);
        }

        public void ForceUpdate(double value, string? valueTip = null, string? status = null)
        {
            if (Upadate(value, valueTip, status) is NotificationUpdateResult.NotificationNotFound)
            {
                Show();
            }
        }

        public ProgressBar(string title) : base(title)
        {
        }
    }
}
