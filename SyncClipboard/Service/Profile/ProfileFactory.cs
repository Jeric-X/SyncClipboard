
using SyncClipboard.Service;
using SyncClipboard.Utility;
using System;
using System.Drawing;
using System.Threading;
using System.Web.Script.Serialization;
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
            String text = "";
            Image image = null;

            LocalClipboardLocker.Lock();
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    IDataObject ClipboardData = Clipboard.GetDataObject();
                    image = (Image)ClipboardData.GetData(DataFormats.Bitmap);
                    text = (string)ClipboardData.GetData(DataFormats.Text);
                    break;
                }
                catch
                {
                    Thread.Sleep(500);
                }
            }

            LocalClipboardLocker.Unlock();
            // if (image != null)
            // {
            //     return new ImageProfile(image);
            // }

            if (text != "" && text != null)
            {
                return new TextProfile(text);
            }

            return null;
        }

        public class JsonProfile
        {
            public String File { get; set; }
            public String Clipboard { get; set; }
            public String Type { get; set; }
        }

        public static Profile CreateFromRemote()
        {
            Log.Write("[PULL] " + Config.GetProfileUrl());
            String httpReply = HttpWebResponseUtility.GetText(Config.GetProfileUrl(), Config.TimeOut, Config.GetHttpAuthHeader());
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

            if (jsonProfile.Clipboard != null || jsonProfile.Clipboard != "")
            {
                return new TextProfile(jsonProfile.Clipboard);
            }

            return null;
        }
    }
}
