using System;
using System.Threading.Tasks;
using System.Text.Json;
using System.Windows.Forms;
using SyncClipboard.Utility.Web;
using static SyncClipboard.Service.ProfileType;
using System.Threading;
using SyncClipboard.Utility.Notification;
using Button = SyncClipboard.Utility.Notification.Button;
#nullable enable

namespace SyncClipboard.Service
{
    public abstract class Profile
    {
        public String FileName { get; set; } = "";
        public String Text { get; set; } = "";

        //public ClipboardType Type { get; set; }

        protected readonly SynchronizationContext? MainThreadSynContext = SynchronizationContext.Current;

        public abstract ClipboardType GetProfileType();
        protected abstract DataObject? CreateDataObject();
        public abstract string ToolTip();
        public abstract Task UploadProfileAsync(IWebDav webdav, CancellationToken cancelToken);
        public virtual Action? ExecuteProfile()
        {
            return null;
        }

        public virtual Task BeforeSetLocal(CancellationToken cancelToken,
            IProgress<HttpDownloadProgress>? progress = null)
        {
            return Task.CompletedTask;
        }

        protected virtual void AfterSetLocal()
        {
            Toast.SendText("剪切板同步成功", Text);
        }

        public void SetLocalClipboard(CancellationToken? cancelToken, bool notify = true)
        {
            var dataObject = CreateDataObject();
            if (dataObject is null)
            {
                return;
            }

            cancelToken?.ThrowIfCancellationRequested();
            lock (SyncService.localProfilemutex)
            {
                if (MainThreadSynContext == SynchronizationContext.Current)
                {
                    Clipboard.SetDataObject(dataObject, true);
                }
                else
                {
                    MainThreadSynContext?.Send((_) => Clipboard.SetDataObject(dataObject, true), null);
                }
            }
            if (notify)
            {
                AfterSetLocal();
            }
        }

        static private string ClipBoardTypeToString(ClipboardType type)
        {
            return Enum.GetName(typeof(ClipboardType), type) ?? "Undefined";
        }

        public string ToJsonString()
        {
            JsonProfile jsonProfile = new()
            {
                File = FileName,
                Clipboard = Text,
                Type = ClipBoardTypeToString(GetProfileType())
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
            return new Button("复制", new(Guid.NewGuid().ToString(), (_) => SetLocalClipboard(null, false)));
        }
    }
}
