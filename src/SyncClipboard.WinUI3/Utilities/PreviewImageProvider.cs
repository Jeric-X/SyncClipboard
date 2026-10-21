using Microsoft.UI.Xaml.Media.Imaging;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Images;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace SyncClipboard.WinUI3.Utilities;

internal static class PreviewImageProvider
{
    private static readonly PreviewImageLoadLimiter DefaultLimiter = new();

    internal sealed record DecodedImage(BitmapImage? Bitmap, PreviewImageSize Size, (uint Width, uint Height) Dimensions);

    internal static PreviewImageCache<PreviewImageKey, DecodedImage> CreateCache(ConfigManager? config)
    {
        var metadata = new PreviewImageMetadataCache<(uint Width, uint Height)>();
        var limiter = new PreviewImageLoadLimiter();
        config?.GetAndListenConfig<PerformanceConfig>(value => limiter.SetLimit(Math.Max(1, value.HistoryImageLoadConcurrency)));
        var cache = new PreviewImageCache<PreviewImageKey, DecodedImage>(0, async (key, token) =>
        {
            var (bitmap, size, dimensions) = await DecodeAsync(key.Path, key.IsThumbnail, key.Scale, token,
                sizeOnly: key.SizeOnly, cachedDimensions: metadata.Get(key), limiter: limiter);
            metadata.Set(key, dimensions);
            return new DecodedImage(bitmap, size, dimensions);
        }, image => image.Bitmap is null ? 256 : (long)image.Size.PixelWidth * image.Size.PixelHeight * 4,
            _ => { }); // BitmapImage has no Dispose; removing all references allows WinUI to reclaim it.
        if (config is not null)
            cache.FollowPerformanceConfiguration(config);
        return cache;
    }

    internal static async Task<(BitmapImage? Bitmap, PreviewImageSize Size, (uint Width, uint Height) Dimensions)> DecodeAsync(
        string path, bool thumbnail, double scale, CancellationToken token,
        Func<IRandomAccessStream, Task<(uint Width, uint Height)>>? readHeader = null, bool sizeOnly = false,
        (uint Width, uint Height)? cachedDimensions = null, PreviewImageLoadLimiter? limiter = null)
    {
        using var permit = await (limiter ?? DefaultLimiter).AcquireAsync(token);
        using var file = await Task.Run(() => File.OpenRead(path), token);
        using var stream = file.AsRandomAccessStream();
        (uint Width, uint Height) dimensions = default;
        try
        {
            dimensions = cachedDimensions ?? await (readHeader ?? ReadDimensionsAsync)(stream);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            dimensions = default;
        }

        token.ThrowIfCancellationRequested();
        stream.Seek(0);
        var size = PreviewImageSize.Calculate(dimensions.Width, dimensions.Height, thumbnail, scale);
        if (sizeOnly)
            return (null, size, dimensions);

        var bitmap = new BitmapImage
        {
            DecodePixelType = DecodePixelType.Physical,
            DecodePixelWidth = size.PixelWidth,
            DecodePixelHeight = size.PixelHeight
        };
        await bitmap.SetSourceAsync(stream);
        token.ThrowIfCancellationRequested();
        return (bitmap, size, dimensions);
    }

    private static Task<(uint Width, uint Height)> ReadDimensionsAsync(IRandomAccessStream stream) => Task.Run(async () =>
    {
        var decoder = await BitmapDecoder.CreateAsync(stream);
        return (decoder.OrientedPixelWidth, decoder.OrientedPixelHeight);
    });
}
