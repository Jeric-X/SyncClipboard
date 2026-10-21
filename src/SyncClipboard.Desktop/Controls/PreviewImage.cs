using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SyncClipboard.Core;
using SyncClipboard.Core.Utilities.Images;
using Microsoft.Extensions.DependencyInjection;
using static SyncClipboard.Desktop.Utilities.PreviewImageProvider;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.Controls;

/// <summary>
/// Displays a leased preview. Hiding or detaching returns the lease; hiding its window does not.
/// </summary>
public sealed class PreviewImage : Image
{
    private readonly PreviewImageCache<PreviewImageKey, DecodedImage> _cache;
    private readonly bool _checkFileVersion;
    private CancellationTokenSource? _request;
    private PreviewImageLease<DecodedImage>? _lease;
    private bool _attached;
    private (string Path, PixelSize Dimensions)? _cachedSize;

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

    public PreviewImage() : this(AppCore.TryGetCurrent()?.Services
        .GetService<PreviewImageCache<PreviewImageKey, DecodedImage>>() ?? CreateCache(null))
    { }

    internal PreviewImage(PreviewImageCache<PreviewImageKey, DecodedImage> cache)
    {
        _cache = cache;
        _checkFileVersion = true;
        Stretch = Stretch.Uniform;
    }

    internal PreviewImage(Func<string, DecodeOptions, CancellationToken, Task<DecodedImage>> load)
    {
        _cache = CreateUncached((key, token) => load(key.Path,
            new DecodeOptions(new Size(key.Width, key.Height), key.Scale, key.MaxPixelWidth, key.SizeOnly,
                _cachedSize?.Path == key.Path ? _cachedSize?.Dimensions : null), token));
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
        _lease?.Dispose();
        _lease = null;
        Width = double.NaN;
        Height = double.NaN;

        if (_cachedSize?.Path != FilePath)
            _cachedSize = null;

        if (!_attached || string.IsNullOrEmpty(FilePath))
        {
            return;
        }

        var scale = ScaleWithRenderScaling ? TopLevel.GetTopLevel(this)?.RenderScaling ?? 1 : 1;
        var options = new DecodeOptions(DecodeBounds, scale, MaxPixelWidth, Dimensions: _cachedSize?.Dimensions);
        if (SizeToImage && _cachedSize is { } cached)
        {
            try
            {
                var (displaySize, _) = GetSizes(cached.Dimensions, options);
                Width = displaySize.Width;
                Height = displaySize.Height;
            }
            catch (InvalidDataException)
            {
                return;
            }
        }

        if (!IsVisible && (!SizeToImage || _cachedSize is not null))
            return;

        _request = new CancellationTokenSource();
        PendingLoad = ShowImageAsync(FilePath, options with { SizeOnly = !IsVisible }, _request.Token);
    }

    private async Task ShowImageAsync(string path, DecodeOptions options, CancellationToken token)
    {
        PreviewImageLease<DecodedImage>? lease = null;
        try
        {
            var key = _checkFileVersion
                ? await Task.Run(() => PreviewImageKey.Create(path, options.Bounds.Width, options.Bounds.Height,
                    options.Scale, options.MaxPixelWidth, options.SizeOnly), token)
                : new PreviewImageKey(path, 0, 0, options.Bounds.Width, options.Bounds.Height, options.Scale,
                    options.MaxPixelWidth, options.SizeOnly);
            token.ThrowIfCancellationRequested();
            _cache.Invalidate(old => old.IsOlderVersionOf(key));
            lease = await _cache.AcquireAsync(key, token);
            var decoded = lease.Image;
            // Decoding itself cannot be interrupted. Never display a late result after hiding the control,
            // refreshing the list, or replacing the file path.
            token.ThrowIfCancellationRequested();
            _cachedSize = (path, decoded.Dimensions);
            if (SizeToImage)
            {
                Width = decoded.DisplaySize.Width;
                Height = decoded.DisplaySize.Height;
            }
            _lease = lease;
            Source = decoded.Bitmap;
            lease = null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            if (!token.IsCancellationRequested)
            {
                _cache.Invalidate(key => key.Path == Path.GetFullPath(path));
                FileMissing?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            AppCore.TryGetCurrent()?.Logger.Write("PreviewImage", $"Failed to load preview: {ex.Message}");
        }
        finally
        {
            lease?.Dispose();
        }
    }

}
