
using SyncClipboard.Utility;
using System;
using System.Drawing;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using static SyncClipboard.Service.ProfileType;

namespace SyncClipboard.Service
{
    static class ProfileFactory
    {
        private struct LocalClipboard
        {
            public string Text;
            //public string Html;
            public Image Image;
            public string[] Files;
        }

        private static readonly string[] imageExtensions = { ".jpg", ".jpeg", ".gif", ".bmp", ".png" };

        public static Profile CreateFromLocal()
        {
            var localClipboard = GetLocalClipboard();

            if (localClipboard.Files != null)
            {
                var filename = localClipboard.Files[0];
                if (System.IO.File.Exists(filename))
                {
                    if (FileIsImage(filename))
                    {
                        return new ImageProfile(filename);
                    }
                    return new FileProfile(filename);
                }
            }

            if (localClipboard.Image != null)
            {
                return ImageProfile.CreateFromImage(localClipboard.Image);
            }

            if (localClipboard.Text != "" && localClipboard.Text != null)
            {
                return new TextProfile(localClipboard.Text);
            }

            return new UnkonwnProfile();
        }

        private static bool FileIsImage(string filename)
        {
            string extension = System.IO.Path.GetExtension(filename);
            foreach (var imageExtension in imageExtensions)
            {
                if (imageExtension.Equals(extension, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static LocalClipboard GetLocalClipboard()
        {
            LocalClipboard localClipboard = new LocalClipboard();

            for (int i = 0; i < 3; i++)
            {
                LocalClipboardLocker.Lock();
                try
                {
                    IDataObject ClipboardData = Clipboard.GetDataObject();
                    if (ClipboardData is null)
                    {
                        return localClipboard;
                    }
                    localClipboard.Image = (Image)ClipboardData.GetData(DataFormats.Bitmap);
                    localClipboard.Text = (string)ClipboardData.GetData(DataFormats.Text);
                    localClipboard.Files = (string[])ClipboardData.GetData(DataFormats.FileDrop);
                    //localClipboard.Html = (string)ClipboardData.GetData(DataFormats.Html);
                    break;
                }
                catch
                {
                    Thread.Sleep(200);
                }
                finally
                {
                    LocalClipboardLocker.Unlock();
                }
            }

            return localClipboard;
        }

        public static Profile CreateFromRemote()
        {
            Log.Write("[PULL] " + UserConfig.GetProfileUrl());
            String httpReply = Program.webDav.GetText(SyncService.REMOTE_RECORD_FILE);
            Log.Write("[PULL] json " + httpReply);

            JsonProfile jsonProfile = null;
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                jsonProfile = serializer.Deserialize<JsonProfile>(httpReply);
            }
            catch (ArgumentException)
            {
                Log.Write("Existed profile file's format is wrong");
                throw new Exception("failed to connect remote server");
            }

            ClipboardType type = StringToClipBoardType(jsonProfile.Type);
            return GetProfileBy(type, jsonProfile);
        }

        private static Profile GetProfileBy(ClipboardType type, JsonProfile jsonProfile)
        {
            switch (type)
            {
                case ClipboardType.Text:
                    return new TextProfile(jsonProfile.Clipboard);
                case ClipboardType.File:
                    {
                        if (FileIsImage(jsonProfile.File))
                        {
                            return new ImageProfile(jsonProfile);
                        }
                        return new FileProfile(jsonProfile);
                    }
                case ClipboardType.Image:
                    return new ImageProfile(jsonProfile);
            }

            return null;
        }
    }
}
