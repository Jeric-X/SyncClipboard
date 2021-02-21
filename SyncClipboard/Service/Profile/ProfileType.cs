using System;

namespace SyncClipboard.Service
{
    class ProfileType
    {
        public enum ClipboardType
        {
            Text,
            File,
            Image,
            None
        };

        public static Type GetProfileClassType(ClipboardType type)
        {
            switch (type)
            {
                case ClipboardType.Text:
                    return typeof(TextProfile);
                case ClipboardType.File:
                    return typeof(FileProfile);
                case ClipboardType.Image:
                    return typeof(ImageProfile);
            }

            return typeof(TextProfile);
        }
    }
}
