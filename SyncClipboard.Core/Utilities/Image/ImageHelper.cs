using ImageMagick;

namespace SyncClipboard.Core.Utilities.Image
{
    public static class ImageHelper
    {
        public static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".gif", ".bmp", ".png" };
        public static readonly string[] ExImageExtensions = { ".heic", ".heif", ".webp", ".avif" };

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

        public static async Task<string> CompatibilityCast(string filePath, string newFileDir, CancellationToken cancelToken)
        {
            using var image = new MagickImageCollection();
            await image.ReadAsync(filePath, cancelToken);
            var newPath = Path.Combine(newFileDir, Path.GetFileNameWithoutExtension(filePath));
            if (image.Count >= 2)
            {
                image.Coalesce();
                newPath += ".gif";
            }
            else
            {
                newPath += ".jpg";
            }
            await image.WriteAsync(newPath, cancelToken);
            return newPath;
        }
    }
}