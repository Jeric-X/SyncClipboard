
using SyncClipboard.Utility;
using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SyncClipboard.Utility.Web;
using SyncClipboard.Utility.Image;
using static SyncClipboard.Service.ProfileType;
using System.Net.Http;
using System.Net;
using System.IO;
#nullable enable

namespace SyncClipboard.Service
{
    public static class ProfileFactory
    {
        public struct LocalClipboard
        {
            public string? Text;
            public string? Html;
            public Image? Image;
            public string[]? Files;
            public DragDropEffects? Effects;
        }

        public static Profile CreateFromLocal(out LocalClipboard localClipboard)
        {
            localClipboard = GetLocalClipboard();

            if (localClipboard.Files != null)
            {
                var filename = localClipboard.Files[0];
                if (System.IO.File.Exists(filename))
                {
                    if (ImageHelper.FileIsImage(filename))
                    {
                        return new ImageProfile(filename);
                    }
                    return new FileProfile(filename);
                }
            }

            if (localClipboard.Text != null)
            {
                return new TextProfile(localClipboard.Text);
            }

            if (localClipboard.Image != null)
            {
                return ImageProfile.CreateFromImage(localClipboard.Image);
            }

            return new UnkonwnProfile();
        }

        public static Profile CreateFromLocal()
        {
            return CreateFromLocal(out _);
        }

        private static LocalClipboard GetLocalClipboard()
        {
            LocalClipboard localClipboard = new();

            for (int i = 0; i < 3; i++)
            {
                lock (SyncService.localProfilemutex)
                {
                    try
                    {
                        IDataObject ClipboardData = Clipboard.GetDataObject();
                        if (ClipboardData is null)
                        {
                            return localClipboard;
                        }
                        if (ClipboardData.GetFormats().Length == 0)
                        {
                            localClipboard.Text = "";
                        }
                        localClipboard.Image = (Image)ClipboardData.GetData(DataFormats.Bitmap);
                        localClipboard.Text = (string)ClipboardData.GetData(DataFormats.Text) ?? localClipboard.Text;
                        localClipboard.Files = (string[])ClipboardData.GetData(DataFormats.FileDrop);
                        localClipboard.Html = (string)ClipboardData.GetData(DataFormats.Html);
                        localClipboard.Effects = (DragDropEffects?)(ClipboardData.GetData("Preferred DropEffect") as MemoryStream)?.ReadByte();
                        break;
                    }
                    catch
                    {
                        Thread.Sleep(200);
                    }
                }
            }

            return localClipboard;
        }

        public static async Task<Profile> CreateFromRemote(IWebDav webDav, CancellationToken cancelToken)
        {
            JsonProfile? jsonProfile;
            try
            {
                jsonProfile = await webDav.GetJson<JsonProfile>(SyncService.REMOTE_RECORD_FILE, cancelToken);
            }
            catch (Exception ex)
            {
                if (ex is HttpRequestException && (ex as HttpRequestException)?.StatusCode == HttpStatusCode.NotFound)
                {
                    var blankProfile = new TextProfile("");
                    await blankProfile.UploadProfileAsync(webDav, cancelToken);
                    return blankProfile;
                }
                Log.Write("CreateFromRemote failed");
                throw;
            }

            ArgumentNullException.ThrowIfNull(jsonProfile);
            ClipboardType type = StringToClipBoardType(jsonProfile.Type);
            return GetProfileBy(type, jsonProfile, webDav);
        }

        private static Profile GetProfileBy(ClipboardType type, JsonProfile jsonProfile, IWebDav webDav)
        {
            switch (type)
            {
                case ClipboardType.Text:
                    return new TextProfile(jsonProfile.Clipboard);
                case ClipboardType.File:
                    {
                        if (ImageHelper.FileIsImage(jsonProfile.File))
                        {
                            return new ImageProfile(jsonProfile, webDav);
                        }
                        return new FileProfile(jsonProfile, webDav);
                    }
                case ClipboardType.Image:
                    return new ImageProfile(jsonProfile, webDav);
            }

            return new UnkonwnProfile();
        }
    }
}
