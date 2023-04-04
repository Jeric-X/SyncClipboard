using System;

namespace SyncClipboard.Utility.Image
{
    public static class ImageHelper
    {
        public static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".gif", ".bmp", ".png" };
        public static readonly string[] ExImageExtensions = { ".heic", ".heif", ".webp" };

        public static bool FileIsImage(string filename)
        {
            string extension = System.IO.Path.GetExtension(filename);
            foreach (var imageExtension in ImageExtensions)
            {
                if (imageExtension.Equals(extension, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsComplexImage(string filename)
        {
            string extension = System.IO.Path.GetExtension(filename);
            foreach (var imageExtension in ExImageExtensions)
            {
                if (imageExtension.Equals(extension, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}