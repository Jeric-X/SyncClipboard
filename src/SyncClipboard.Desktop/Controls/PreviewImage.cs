using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;
using SyncClipboard.Core;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.Controls;

/// <summary>
/// Owns a bounded image preview. Hiding or detaching the control releases its bitmap; hiding its window does not.
/// </summary>
public sealed class PreviewImage : Image
{
    private const long FullDecodeFileLimit = 10L * 1024 * 1024;
    private static readonly SemaphoreSlim DecodeSlots = new(Environment.ProcessorCount);
    private readonly Func<string, DecodeOptions, CancellationToken, Task<DecodedImage>> _load;
    private CancellationTokenSource? _request;
    private Bitmap? _bitmap;
    private bool _attached;

    public static readonly StyledProperty<string?> FilePathProperty =
        AvaloniaProperty.Register<PreviewImage, string?>(nameof(FilePath));

    public string? FilePath
    {
        get => GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    // Bounds are physical pixels unless ScaleWithRenderScaling is enabled for a list thumbnail.
    public static readonly StyledProperty<Size> DecodeBoundsProperty =
        AvaloniaProperty.Register<PreviewImage, Size>(nameof(DecodeBounds), new Size(2048, 2048),
            validate: size => double.IsFinite(size.Width) && double.IsFinite(size.Height) && size.Width >= 1 && size.Height >= 1);
    public static readonly StyledProperty<bool> ScaleWithRenderScalingProperty =
        AvaloniaProperty.Register<PreviewImage, bool>(nameof(ScaleWithRenderScaling));
    public static readonly StyledProperty<int> MaxPixelWidthProperty =
        AvaloniaProperty.Register<PreviewImage, int>(nameof(MaxPixelWidth), int.MaxValue, validate: value => value > 0);
    public static readonly StyledProperty<bool> SizeToImageProperty =
        AvaloniaProperty.Register<PreviewImage, bool>(nameof(SizeToImage));

    public Size DecodeBounds
    {
        get => GetValue(DecodeBoundsProperty);
        set => SetValue(DecodeBoundsProperty, value);
    }

    public bool ScaleWithRenderScaling
    {
        get => GetValue(ScaleWithRenderScalingProperty);
        set => SetValue(ScaleWithRenderScalingProperty, value);
    }

    public int MaxPixelWidth
    {
        get => GetValue(MaxPixelWidthProperty);
        set => SetValue(MaxPixelWidthProperty, value);
    }

    public bool SizeToImage
    {
        get => GetValue(SizeToImageProperty);
        set => SetValue(SizeToImageProperty, value);
    }

    internal event EventHandler? FileMissing;

    internal Task PendingLoad { get; private set; } = Task.CompletedTask;

    public PreviewImage() : this(LoadAsync) { }

    internal PreviewImage(Func<string, DecodeOptions, CancellationToken, Task<DecodedImage>> load)
    {
        _load = load;
        Stretch = Stretch.Uniform;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FilePathProperty || change.Property == IsVisibleProperty ||
            change.Property == DecodeBoundsProperty || change.Property == ScaleWithRenderScalingProperty ||
            change.Property == SizeToImageProperty || change.Property == MaxPixelWidthProperty)
        {
            UpdateImage();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        UpdateImage();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        UpdateImage();
        base.OnDetachedFromVisualTree(e);
    }

    private void UpdateImage()
    {
        _request?.Cancel();
        _request?.Dispose();
        _request = null;
        Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
        Width = double.NaN;
        Height = double.NaN;

        if (!_attached || !IsVisible || string.IsNullOrEmpty(FilePath))
        {
            return;
        }

        _request = new CancellationTokenSource();
        var scale = ScaleWithRenderScaling ? TopLevel.GetTopLevel(this)?.RenderScaling ?? 1 : 1;
        PendingLoad = ShowImageAsync(FilePath, new DecodeOptions(DecodeBounds, scale, MaxPixelWidth), _request.Token);
    }

    private async Task ShowImageAsync(string path, DecodeOptions options, CancellationToken token)
    {
        DecodedImage? decoded = null;
        try
        {
            decoded = await _load(path, options, token);
            // Decoding itself cannot be interrupted. Never retain a late result after hiding the control,
            // refreshing the list, or replacing the file path.
            token.ThrowIfCancellationRequested();
            if (SizeToImage)
            {
                Width = decoded.DisplaySize.Width;
                Height = decoded.DisplaySize.Height;
            }
            _bitmap = decoded.Bitmap;
            Source = _bitmap;
            decoded = null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            if (!token.IsCancellationRequested)
            {
                FileMissing?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            AppCore.TryGetCurrent()?.Logger.Write("PreviewImage", $"Failed to load preview: {ex.Message}");
        }
        finally
        {
            decoded?.Bitmap.Dispose();
        }
    }

    private static async Task<DecodedImage> LoadAsync(string path, DecodeOptions options, CancellationToken token)
    {
        await DecodeSlots.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                return Decode(path, options);
            }, token).ConfigureAwait(false);
        }
        finally
        {
            DecodeSlots.Release();
        }
    }

    internal static DecodedImage Decode(string path, DecodeOptions options, Func<Stream, PixelSize?>? readHeader = null)
    {
        using var stream = File.OpenRead(path);
        PixelSize? dimensions;
        try
        {
            dimensions = (readHeader ?? ReadDimensions)(stream);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            dimensions = null;
        }

        stream.Position = 0;
        if (dimensions is not { Width: > 0, Height: > 0 } size)
        {
            return DecodeFullImage(stream, options);
        }

        var (displaySize, pixelSize) = GetSizes(size, options);
        // The proportional decoder truncates the short dimension to zero for extreme aspect ratios.
        var ratio = Math.Min(1, Math.Min(Math.Min(options.Bounds.Width * options.Scale, options.MaxPixelWidth) / size.Width,
            options.Bounds.Height * options.Scale / size.Height));
        if (size.Width * ratio < 1 || size.Height * ratio < 1)
        {
            throw new InvalidDataException("Scaled image dimensions are smaller than one pixel.");
        }

        var bitmap = (double)size.Width * pixelSize.Height >= (double)size.Height * pixelSize.Width
            ? Bitmap.DecodeToWidth(stream, pixelSize.Width)
            : Bitmap.DecodeToHeight(stream, pixelSize.Height);
        return new DecodedImage(bitmap, displaySize);
    }

    private static PixelSize? ReadDimensions(Stream stream)
    {
        using var managedStream = new SKManagedStream(stream, false);
        using var codec = SKCodec.Create(managedStream);
        return codec is null ? null : new PixelSize(codec.Info.Width, codec.Info.Height);
    }

    private static DecodedImage DecodeFullImage(Stream stream, DecodeOptions options)
    {
        // A failed header must never trigger an unbounded full decode of a large file.
        if (stream.Length >= FullDecodeFileLimit)
        {
            throw new InvalidDataException("Image requires full decoding but is not smaller than 10 MiB.");
        }

        using var original = new Bitmap(stream);
        var (displaySize, pixelSize) = GetSizes(original.PixelSize, options);
        return new DecodedImage(original.CreateScaledBitmap(pixelSize), displaySize);
    }

    private static (Size DisplaySize, PixelSize PixelSize) GetSizes(PixelSize size, DecodeOptions options)
    {
        var displayRatio = Math.Min(1, Math.Min(options.Bounds.Width / size.Width, options.Bounds.Height / size.Height));
        var displaySize = new Size(size.Width * displayRatio, size.Height * displayRatio);
        var pixelRatio = Math.Min(Math.Min(1, displayRatio * options.Scale), (double)options.MaxPixelWidth / size.Width);
        var pixelSize = new PixelSize(Math.Max(1, (int)Math.Floor(size.Width * pixelRatio)),
            Math.Max(1, (int)Math.Floor(size.Height * pixelRatio)));
        return (displaySize, pixelSize);
    }

    internal readonly record struct DecodeOptions(Size Bounds, double Scale = 1, int MaxPixelWidth = int.MaxValue);
    internal sealed record DecodedImage(Bitmap Bitmap, Size DisplaySize);
}
