using Microsoft.Toolkit.Uwp.Notifications;
using SyncClipboard.Abstract.Notification;
using Windows.UI.Notifications;

namespace SyncClipboard.Core.Utilities.Notification
{
    public class ToastSession : NotificationSessionBase<string>
    {
        private readonly ToastNotifier _notifer;

        private readonly CallbackHandler<string> _callbackHandler;
        public static string Group => "DEFAULT_GROUP";
        private readonly string _tag = Guid.NewGuid().ToString();

        protected override string? NativeNotificationId => _tag;

        public string Tag => _tag.Length <= 64 ? _tag : _tag[..63];
        public override string Title { get; set; }
        public string? Text1 { get; set; }
        public string? Text2 { get; set; }
        public ToastSession(string title, ToastNotifier notifier, CallbackHandler<string> callbackHandler) : base(callbackHandler)
        {
            Title = title;
            _notifer = notifier;
            _callbackHandler = callbackHandler;
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
            if (Image is not null)
            {
                builder.AddHeroImage(Image);
            }
            foreach (var button in Buttons)
            {
                builder.AddButton(button);
                _callbackHandler.AddButton(Tag, button, this);
            }
            return builder;
        }

        protected virtual ToastNotification GetToast(ToastContentBuilder builder)
        {
            var toast = new ToastNotification(builder.GetToastContent().GetXml())
            {
                Tag = this.Tag,
                Group = Group,
                Data = new NotificationData()
            };
            toast.Data.Values[TOAST_BINDING_TITLE] = Title;
            toast.Data.Values[TOAST_BINDING_TEXT1] = Text1;
            toast.Data.Values[TOAST_BINDING_TEXT2] = Text2;
            toast.Activated += Toast_Activated;
            toast.Dismissed += Toast_Dismissed;
            return toast;
        }

        private void Toast_Activated(ToastNotification sender, object e)
        {
            if (e is not ToastActivatedEventArgs args)
                return;
            if (args.Arguments.Length != 0)
            {
                _callbackHandler.OnActivated(Tag, args.Arguments);
            }
            else
            {
                _callbackHandler.OnClosed(Tag);
            }
        }

        private void Toast_Dismissed(ToastNotification sender, ToastDismissedEventArgs args)
        {
            if (args.Reason is ToastDismissalReason.UserCanceled)
            {
                _callbackHandler.OnClosed(Tag);
            }
        }

        protected override void NativeShow()
        {
            _notifer.Show(GetToast(GetBuilder()));
        }

        protected override void NativeShowSilent()
        {
            _notifer.Show(GetToast(GetBuilder().AddAudio(null, null, true)));
        }

        protected override void NativeRemove()
        {
            ToastNotificationManagerCompat.History.Remove(Tag, Group);
        }
    }
}