
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
            public Image Image;
            public string[] Files;
        }

        public static Profile CreateFromLocal()
        {
            var localClipboard = GetLocalClipboard();

            if (localClipboard.Files != null)
            {
                if (System.IO.File.Exists(localClipboard.Files[0]))
                {
                    return new FileProfile(localClipboard.Files[0]);
                }
            }

            if (localClipboard.Text != "" && localClipboard.Text != null)
            {
                return new TextProfile(localClipboard.Text);
            }

            return new UnkonwnProfile();
        }

        private static LocalClipboard GetLocalClipboard()
        {
            LocalClipboard localClipboard = new LocalClipboard { Text = null, Image = null, Files = null};

            LocalClipboardLocker.Lock();
            for (int i = 0; i < 3; i++)
            {
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
                    break;
                }
                catch
                {
                    Thread.Sleep(200);
                }
            }

            LocalClipboardLocker.Unlock();

            return localClipboard;
        }

        public static Profile CreateFromRemote()
        {
            Log.Write("[PULL] " + Config.GetProfileUrl());
            String httpReply = HttpWebResponseUtility.GetText(Config.GetProfileUrl(), Config.GetHttpAuthHeader());
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
                    return new FileProfile(jsonProfile);
            }

            return null;
        }
    }
}
