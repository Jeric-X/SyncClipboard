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
            using var dummyTask = Task.Delay(TimeSpan.FromMinutes(5), cancelToken);
            using var convertTask = Task.Run(() => ConverWithMagick(filePath, newFileDir), cancelToken);
            await Task.WhenAny(convertTask, dummyTask);
            return convertTask.IsCompletedSuccessfully ? convertTask.Result : throw new OperationCanceledException();
        }

        private static string ConverWithMagick(string filePath, string newFileDir)
        {
            using var image = new MagickImageCollection();
            image.Read(filePath);
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
            image.Write(newPath);
            return newPath;
        }
    }
}