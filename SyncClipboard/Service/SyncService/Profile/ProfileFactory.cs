
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Utilities.Notification;
using SyncClipboard.Utility;
using SyncClipboard.Core.Utilities.Image;
using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static SyncClipboard.Service.ProfileType;
using SyncClipboard.Core.Clipboard;
using DragDropEffects = System.Windows.Forms.DragDropEffects;
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

        public static Profile CreateFromLocal(out LocalClipboard localClipboard, NotificationManager notificationManager)
        {
            localClipboard = GetLocalClipboard();

            if (localClipboard.Files != null)
            {
                var filename = localClipboard.Files[0];
                if (System.IO.File.Exists(filename))
                {
                    if (ImageHelper.FileIsImage(filename))
                    {
                        return new ImageProfile(filename, Global.Logger, Global.UserConfig);
                    }
                    return new FileProfile(filename, Global.Logger, Global.UserConfig);
                }
            }

            if (localClipboard.Text != null)
            {
                return new TextProfile(localClipboard.Text, notificationManager);
            }

            if (localClipboard.Image != null)
            {
                return ImageProfile.CreateFromImage(localClipboard.Image, Global.Logger, Global.UserConfig);
            }

            return new UnkonwnProfile();
        }

        public static Profile CreateFromLocal(NotificationManager notificationManager)
        {
            return CreateFromLocal(out _, notificationManager);
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
                if (ex is HttpRequestException { StatusCode: HttpStatusCode.NotFound })
                {
                    var blankProfile = new TextProfile("");
                    await blankProfile.UploadProfileAsync(webDav, cancelToken);
                    return blankProfile;
                }
                Global.Logger.Write("CreateFromRemote failed");
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
                            return new ImageProfile(jsonProfile, webDav, Global.Logger, Global.UserConfig);
                        }
                        return new FileProfile(jsonProfile, webDav, Global.Logger, Global.UserConfig);
                    }
                case ClipboardType.Image:
                    return new ImageProfile(jsonProfile, webDav, Global.Logger, Global.UserConfig);
            }

            return new UnkonwnProfile();
        }
    }
}
