using Avalonia;
using Avalonia.Media.Imaging;
using SkiaSharp;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities.Images;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.Utilities;

internal static class PreviewImageProvider
{
    internal static PreviewImageCache<PreviewImageKey, DecodedImage> CreateCache(ConfigManager? config)
    {
        var dimensions = new PreviewImageMetadataCache<PixelSize>();
        var limiter = new PreviewImageLoadLimiter();
        config?.GetAndListenConfig<PerformanceConfig>(value => limiter.SetLimit(Math.Max(1, value.HistoryImageLoadConcurrency)));
        var cache = new PreviewImageCache<PreviewImageKey, DecodedImage>(0, async (key, token) =>
        {
            var image = await LoadAsync(key.Path, new DecodeOptions(new Size(key.Width, key.Height),
                key.Scale, key.MaxPixelWidth, key.SizeOnly, dimensions.Get(key)), limiter, token);
            dimensions.Set(key, image.Dimensions);
            return image;
        }, Measure, image => image.Bitmap?.Dispose());
        if (config is not null)
            cache.FollowPerformanceConfiguration(config);
        return cache;
    }

    internal static PreviewImageCache<PreviewImageKey, DecodedImage> CreateUncached(
        Func<PreviewImageKey, CancellationToken, Task<DecodedImage>> load) =>
        new(0, load, Measure, image => image.Bitmap?.Dispose());

    private static long Measure(DecodedImage image) => image.Bitmap is { } bitmap
        ? (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4 : 256;

    private static async Task<DecodedImage> LoadAsync(string path, DecodeOptions options, PreviewImageLoadLimiter limiter, CancellationToken token)
    {
        using var permit = await limiter.AcquireAsync(token).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            return Decode(path, options);
        }, token).ConfigureAwait(false);
    }

    internal static DecodedImage Decode(string path, DecodeOptions options, Func<Stream, PixelSize?>? readHeader = null)
    {
        using var stream = File.OpenRead(path);
        PixelSize? dimensions;
        try
        {
            dimensions = options.Dimensions ?? (readHeader ?? ReadDimensions)(stream);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            dimensions = null;
        }

        stream.Position = 0;
        if (dimensions is not { Width: > 0, Height: > 0 } size)
            throw new InvalidDataException("Image dimensions are unavailable.");

        var (displaySize, pixelSize) = GetSizes(size, options);
        if (options.SizeOnly)
            return new DecodedImage(null, displaySize, size);

        var bitmap = (double)size.Width * pixelSize.Height >= (double)size.Height * pixelSize.Width
            ? Bitmap.DecodeToWidth(stream, pixelSize.Width)
            : Bitmap.DecodeToHeight(stream, pixelSize.Height);
        return new DecodedImage(bitmap, displaySize, size);
    }

    private static PixelSize? ReadDimensions(Stream stream)
    {
        using var managedStream = new SKManagedStream(stream, false);
        using var codec = SKCodec.Create(managedStream);
        return codec is null ? null : new PixelSize(codec.Info.Width, codec.Info.Height);
    }

    internal static (Size DisplaySize, PixelSize PixelSize) GetSizes(PixelSize size, DecodeOptions options)
    {
        var displayRatio = Math.Min(1, Math.Min(options.Bounds.Width / size.Width, options.Bounds.Height / size.Height));
        var displaySize = new Size(size.Width * displayRatio, size.Height * displayRatio);
        var pixelRatio = Math.Min(Math.Min(1, displayRatio * options.Scale), (double)options.MaxPixelWidth / size.Width);
        if (size.Width * pixelRatio < 1 || size.Height * pixelRatio < 1)
            throw new InvalidDataException("Scaled image dimensions are smaller than one pixel.");

        var pixelSize = new PixelSize((int)Math.Floor(size.Width * pixelRatio), (int)Math.Floor(size.Height * pixelRatio));
        return (displaySize, pixelSize);
    }

    internal readonly record struct DecodeOptions(
        Size Bounds, double Scale = 1, int MaxPixelWidth = int.MaxValue, bool SizeOnly = false, PixelSize? Dimensions = null);
    internal sealed record DecodedImage(Bitmap? Bitmap, Size DisplaySize, PixelSize Dimensions);
}
