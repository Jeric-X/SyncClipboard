
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
        public static Profile CreateFromLocal()
        {
            string text = "";
            Image image = null;
            string[] files = null;

            LocalClipboardLocker.Lock();
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    IDataObject ClipboardData = Clipboard.GetDataObject();
                    if (ClipboardData is null)
                    {
                        return null;
                    }
                    image = (Image)ClipboardData.GetData(DataFormats.Bitmap);
                    text = (string)ClipboardData.GetData(DataFormats.Text);
                    files = (string[])ClipboardData.GetData(DataFormats.FileDrop);
                    break;
                }
                catch
                {
                    Thread.Sleep(200);
                }
            }

            LocalClipboardLocker.Unlock();
            // if (image != null)
            // {
            //     return new ImageProfile(image);
            // }

            if (files != null)
            {
                if (System.IO.File.Exists(files[0]))
                {
                    return new FileProfile(files[0]);
                }
            }

            if (text != "" && text != null)
            {
                return new TextProfile(text);
            }

            return new UnkonwnProfile();
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
