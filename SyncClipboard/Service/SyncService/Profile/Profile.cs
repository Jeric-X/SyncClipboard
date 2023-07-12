using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.Notification;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static SyncClipboard.Service.ProfileType3;
using Button = SyncClipboard.Core.Utilities.Notification.Button;
#nullable enable

namespace SyncClipboard.Service
{
    public abstract class Profile
    {
        public string FileName { get; set; } = "";
        public string Text { get; set; } = "";

        #region abstract

        protected abstract IClipboardSetter<Profile> ClipboardSetter { get; set; }
        protected abstract MetaInfomation CreateMetaInformation();

        #endregion

        private readonly SynchronizationContext? MainThreadSynContext = SynchronizationContext.Current;
        public abstract ProfileType Type { get; }

        private MetaInfomation? @metaInfomation;
        public MetaInfomation MetaInfomation
        {
            get
            {
                @metaInfomation ??= CreateMetaInformation();
                return @metaInfomation;
            }
        }

        public abstract string ToolTip();
        public abstract Task UploadProfileAsync(IWebDav webdav, CancellationToken cancelToken);

        public virtual Task BeforeSetLocal(CancellationToken cancelToken,
            IProgress<HttpDownloadProgress>? progress = null)
        {
            return Task.CompletedTask;
        }

        protected virtual void SetNotification(NotificationManager notificationManager)
        {
            notificationManager.SendText("剪切板同步成功", Text);
        }

        public void SetLocalClipboard(NotificationManager? notificationManager = null)
        {
            var ClipboardObjectContainer = ClipboardSetter.CreateClipboardObjectContainer(MetaInfomation);
            if (ClipboardObjectContainer is null)
            {
                return;
            }

            lock (SyncService.localProfilemutex)
            {
                if (MainThreadSynContext == SynchronizationContext.Current)
                {
                    ClipboardSetter.SetLocalClipboard(ClipboardObjectContainer);
                }
                else
                {
                    MainThreadSynContext?.Send((_) => ClipboardSetter.SetLocalClipboard(ClipboardObjectContainer), null);
                }
            }

            if (notificationManager is not null)
            {
                SetNotification(notificationManager);
            }
        }

        public string ToJsonString()
        {
            JsonProfile jsonProfile = new()
            {
                File = FileName,
                Clipboard = Text,
                Type = ProfileTypeHelper.ClipBoardTypeToString(Type)
            };

            return JsonSerializer.Serialize(jsonProfile);
        }

        protected abstract Task<bool> Same(Profile rhs, CancellationToken cancellationToken);

        public static async Task<bool> Same(Profile? lhs, Profile? rhs, CancellationToken cancellationToken)
        {
            if (ReferenceEquals(lhs, rhs))
            {
                return true;
            }

            if (lhs is null)
            {
                return rhs is null;
            }

            if (rhs is null)
            {
                return false;
            }

            if (lhs.GetType() != rhs.GetType())
            {
                return false;
            }

            return await lhs.Same(rhs, cancellationToken);
        }

        public override string ToString()
        {
            string str = "";
            str += "FileName" + FileName;
            str += "Text:" + Text;
            return str;
        }

        protected Button DefaultButton()
        {
            return new Button("复制", new(Guid.NewGuid().ToString(), (_) => SetLocalClipboard()));
        }
    }
}
