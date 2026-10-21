using ImageMagick;

namespace SyncClipboard.Core.Models;

public sealed record HistoryImageMetadata(string FilePath, uint Width, uint Height, bool Rotated)
{
    private static readonly SemaphoreSlim ReadSlots = new(2, 2);
    // Skia measures a GIF's logical canvas; Windows BitmapDecoder measures its first frame.
    public uint CanvasWidth { get; init; } = Width;
    public uint CanvasHeight { get; init; } = Height;

    public static async Task<HistoryImageMetadata?> ReadAsync(string path, CancellationToken token = default)
    {
        await ReadSlots.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var info = new MagickImage();
                    info.Ping(path, new MagickReadSettings { FrameCount = 1 });
                    var rotated = info.Orientation is OrientationType.LeftTop or OrientationType.RightTop
                        or OrientationType.RightBottom or OrientationType.LeftBottom;
                    var canvasWidth = info.Format == MagickFormat.Gif ? info.Page.Width : info.Width;
                    var canvasHeight = info.Format == MagickFormat.Gif ? info.Page.Height : info.Height;
                    return info.Width is > 0 and <= int.MaxValue && info.Height is > 0 and <= int.MaxValue
                        && canvasWidth is > 0 and <= int.MaxValue && canvasHeight is > 0 and <= int.MaxValue
                        ? new HistoryImageMetadata(path, info.Width, info.Height, rotated)
                        {
                            CanvasWidth = canvasWidth,
                            CanvasHeight = canvasHeight
                        }
                        : null;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Keep the record available when its header is missing or unsupported.
                    return null;
                }
            }, token).ConfigureAwait(false);
        }
        finally
        {
            ReadSlots.Release();
        }
    }
}
