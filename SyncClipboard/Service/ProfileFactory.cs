
using SyncClipboard.Utility;
using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace SyncClipboard
{
    static class ProfileFactory
    {
        public enum ClipboardType {
            Text,
            Image,
            None
        };


        public static Profile CreateFromLocal()
        {
            Profile profile = new Profile();

            LocalClipboardLocker.Lock();
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    IDataObject ClipboardData = Clipboard.GetDataObject();
                    profile.image = (Image)ClipboardData.GetData(DataFormats.Bitmap);
                    profile.Text = (string)ClipboardData.GetData(DataFormats.Text);
                    break;
                }
                catch
                {
                    Thread.Sleep(500);
                }
            }

            LocalClipboardLocker.Unlock();
            if (profile.image != null)
            {
                profile.Type = ClipboardType.Image;
            }

            if (profile.Text != "" && profile.Text != null)
            {
                profile.Type = ClipboardType.Text;
            }

            return profile;
        }

        public static Profile CreateFromRemote()
        {
            Log.Write("[PULL] " + Config.GetProfileUrl());
            String jsonProfile = HttpWebResponseUtility.GetText(Config.GetProfileUrl(), Config.TimeOut, Config.GetHttpAuthHeader());
            Log.Write("[PULL] json " + jsonProfile);

            return new Profile(jsonProfile);
        }
    }
}
