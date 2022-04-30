using System;
using System.Collections.Generic;
using Microsoft.Toolkit.Uwp.Notifications;
using Windows.UI.Notifications;
#nullable enable

namespace SyncClipboard.Utility.Notification
{
    public class ToastSession
    {
        public const string DEFAULT_GROUP = "DEFAULT_GROUP";
        public const string DEFAULT_TAG = "DEFAULT_TAG";
        public string Group { get; set; } = DEFAULT_GROUP;
        public string Tag { get; set; } = DEFAULT_TAG;
        public string Title { get; set; }
        public string? Text1 { get; set; }
        public string? Text2 { get; set; }
        public List<Button> Buttons { get; set; } = new();
        public ToastSession(string title)
        {
            Title = title;
        }

        private const string TOAST_BINDING_TITLE = "TOAST_BINDING_TITLE";
        private const string TOAST_BINDING_TEXT1 = "TOAST_BINDING_TEXT1";
        private const string TOAST_BINDING_TEXT2 = "TOAST_BINDING_TEXT2";

        protected virtual ToastContentBuilder GetBuilder()
        {
            var builder = new ToastContentBuilder();
            builder.AddVisualChild(new AdaptiveText() { Text = new BindableString(TOAST_BINDING_TITLE) });
            builder.AddVisualChild(new AdaptiveText() { Text = new BindableString(TOAST_BINDING_TEXT1) });
            builder.AddVisualChild(new AdaptiveText() { Text = new BindableString(TOAST_BINDING_TEXT2) });
            foreach (var button in Buttons)
            {
                builder.AddButton(button);
            }
            return builder;
        }

        protected virtual ToastNotification GetToast(ToastContentBuilder builder)
        {
            var toast = new ToastNotification(builder.GetToastContent().GetXml())
            {
                Tag = this.Tag,
                Group = this.Group,
                Data = new NotificationData()
            };
            toast.Data.Values[TOAST_BINDING_TITLE] = Title;
            toast.Data.Values[TOAST_BINDING_TEXT1] = Text1;
            toast.Data.Values[TOAST_BINDING_TEXT2] = Text2;
            return toast;
        }

        public virtual void Show()
        {
            Toast.Notifer.Show(GetToast(GetBuilder()));
        }

        public virtual void ShowSilent()
        {
            Toast.Notifer.Show(GetToast(GetBuilder().AddAudio(null, null, true)));
        }

        public virtual void Remove()
        {
            ToastNotificationManagerCompat.History.Remove(Tag, Group);
        }
    }
}