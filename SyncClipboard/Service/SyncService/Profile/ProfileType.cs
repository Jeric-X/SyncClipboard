using System;
using SyncClipboard.Utility;

namespace SyncClipboard.Service
{
    class ProfileType
    {
        public enum ClipboardType
        {
            Text,
            File,
            Image,
            Unknown,
            None
        };

        public class JsonProfile
        {
            public String File { get; set; }
            public String Clipboard { get; set; }
            public String Type { get; set; }
        }

        public static ClipboardType StringToClipBoardType(String stringType)
        {
            ClipboardType type = ClipboardType.Text;
            try
            {
                type = (ClipboardType)Enum.Parse(typeof(ClipboardType), stringType);
            }
            catch
            {
                Log.Write("Profile Type is Wrong");
                throw new ArgumentException("Profile Type is Wrong");
            }

            return type;
        }
    }
}
