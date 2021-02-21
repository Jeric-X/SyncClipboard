
using SyncClipboard.Service;
using SyncClipboard.Utility;
using System;
using System.Drawing;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

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
                return new FileProfile(files[0]);
            }

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

            //return ProfileType.GetProfileClassType(jsonProfile.Type)(jsonProfile);

            if (jsonProfile.Clipboard != null || jsonProfile.Clipboard != "")
            {
                return new TextProfile(jsonProfile.Clipboard);
            }

            return null;
        }
    }
}
