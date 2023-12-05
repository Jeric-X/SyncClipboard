using Microsoft.Toolkit.Uwp.Notifications;
using SyncClipboard.Abstract;
using Windows.UI.Notifications;

namespace SyncClipboard.Core.Utilities.Notification
{
    public class ToastSession
    {
        private readonly ToastNotifier _notifer;

        public const string DEFAULT_GROUP = "DEFAULT_GROUP";
        public const string DEFAULT_TAG = "DEFAULT_TAG";
        private string group = DEFAULT_GROUP;
        public string Group { get => group; set => group = value.Length <= 64 ? value : value[..63]; }
        private string tag = DEFAULT_TAG;
        public string Tag { get => tag; set => tag = value.Length <= 64 ? value : value[..63]; }
        public string Title { get; set; }
        public string? Text1 { get; set; }
        public string? Text2 { get; set; }
        public Uri? Image { get; set; }
        public List<Button> Buttons { get; set; } = new();
        public ToastSession(string title, ToastNotifier notifier)
        {
            Title = title;
            _notifer = notifier;
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
            _notifer.Show(GetToast(GetBuilder()));
        }

        public virtual void ShowSilent()
        {
            _notifer.Show(GetToast(GetBuilder().AddAudio(null, null, true)));
        }

        public virtual void Remove()
        {
            try
            {
                ToastNotificationManagerCompat.History.Remove(Tag, Group);
            }
            catch
            {
            }
        }
    }
}