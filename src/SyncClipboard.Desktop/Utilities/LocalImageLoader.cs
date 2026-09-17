using AsyncImageLoader.Loaders;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using System.IO;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.Utilities;

internal sealed class LocalImageLoader : RamCachedWebImageLoader
{
    protected override Task<Bitmap?> LoadAsync(string url, IStorageProvider? storageProvider) => LoadAsync(url);

    protected override Task<Bitmap?> LoadAsync(string url)
    {
        // ImageLoader invokes this on a worker thread; missing local files must not fall back to HTTP.
        try
        {
            using var stream = File.OpenRead(url);
            return Task.FromResult<Bitmap?>(new Bitmap(stream));
        }
        catch (FileNotFoundException)
        {
            return Task.FromResult<Bitmap?>(null);
        }
        catch (DirectoryNotFoundException)
        {
            return Task.FromResult<Bitmap?>(null);
        }
    }
}
