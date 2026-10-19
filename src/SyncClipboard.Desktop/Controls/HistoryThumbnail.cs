using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SkiaSharp;
using SyncClipboard.Core;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.Desktop.Controls;

/// <summary>
/// Owns a history row's thumbnail. Hidden or removed rows retain no decoded image.
/// </summary>
public sealed class HistoryThumbnail : Image
{
    private const double ThumbnailWidth = 400;
    private const double ThumbnailHeight = 202;
    private static readonly SemaphoreSlim DecodeSlots = new(2);
    private readonly Func<string, double, CancellationToken, Task<Thumbnail>> _load;
    private CancellationTokenSource? _request;
    private Bitmap? _bitmap;
    private bool _attached;
    private Visual[] _ancestors = [];

    public static readonly StyledProperty<string?> FilePathProperty =
        AvaloniaProperty.Register<HistoryThumbnail, string?>(nameof(FilePath));

    public string? FilePath
    {
        get => GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    internal Task PendingLoad { get; private set; } = Task.CompletedTask;

    public HistoryThumbnail() : this(LoadAsync) { }

    internal HistoryThumbnail(Func<string, double, CancellationToken, Task<Thumbnail>> load)
    {
        _load = load;
        Stretch = Stretch.Uniform;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FilePathProperty || change.Property == IsVisibleProperty)
        {
            UpdateImage();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _ancestors = this.GetVisualAncestors().ToArray();
        foreach (var ancestor in _ancestors)
        {
            ancestor.PropertyChanged += AncestorPropertyChanged;
        }
        _attached = true;
        UpdateImage();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        foreach (var ancestor in _ancestors)
        {
            ancestor.PropertyChanged -= AncestorPropertyChanged;
        }
        _ancestors = [];
        _attached = false;
        UpdateImage();
        base.OnDetachedFromVisualTree(e);
    }

    private void AncestorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty)
        {
            UpdateImage();
        }
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

        if (!_attached || !IsVisible || _ancestors.Any(ancestor => !ancestor.IsVisible) || string.IsNullOrEmpty(FilePath))
        {
            return;
        }

        _request = new CancellationTokenSource();
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        PendingLoad = ShowImageAsync(FilePath, scale, _request.Token);
    }

    private async Task ShowImageAsync(string path, double scale, CancellationToken token)
    {
        Thumbnail? thumbnail = null;
        try
        {
            thumbnail = await _load(path, scale, token);
            // Decoding itself cannot be interrupted. Never retain a late result after hiding,
            // refreshing the list, or replacing the file path.
            token.ThrowIfCancellationRequested();
            Width = thumbnail.DisplaySize.Width;
            Height = thumbnail.DisplaySize.Height;
            _bitmap = thumbnail.Bitmap;
            Source = _bitmap;
            thumbnail = null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            AppCore.TryGetCurrent()?.Logger.Write("HistoryThumbnail", $"Failed to load thumbnail: {ex.Message}");
        }
        finally
        {
            thumbnail?.Bitmap.Dispose();
        }
    }

    private static async Task<Thumbnail> LoadAsync(string path, double scale, CancellationToken token)
    {
        await DecodeSlots.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                return Decode(path, scale);
            }, token).ConfigureAwait(false);
        }
        finally
        {
            DecodeSlots.Release();
        }
    }

    internal static Thumbnail Decode(string path, double scale)
    {
        // Read only the header to choose the decode size; don't decode the full image first.
        using var codec = SKCodec.Create(path) ?? throw new InvalidDataException("Unsupported image format.");
        var info = codec.Info;
        var displayRatio = Math.Min(1, Math.Min(ThumbnailWidth / info.Width, ThumbnailHeight / info.Height));
        var displaySize = new Size(info.Width * displayRatio, info.Height * displayRatio);
        var pixelRatio = Math.Min(1, displayRatio * scale);
        using var stream = File.OpenRead(path);
        // Avalonia's proportional decoder truncates the other dimension to zero for
        // extreme aspect ratios. Clamp both dimensions explicitly in that case.
        if (info.Width * pixelRatio < 1 || info.Height * pixelRatio < 1)
        {
            using var original = new Bitmap(stream);
            var size = new PixelSize(Math.Max(1, (int)(info.Width * pixelRatio)),
                Math.Max(1, (int)(info.Height * pixelRatio)));
            return new Thumbnail(original.CreateScaledBitmap(size), displaySize);
        }

        // Decode along the limiting dimension, including very narrow/tall images.
        var bitmap = info.Width * ThumbnailHeight >= info.Height * ThumbnailWidth
            ? Bitmap.DecodeToWidth(stream, Math.Max(1, (int)Math.Floor(info.Width * pixelRatio)))
            : Bitmap.DecodeToHeight(stream, Math.Max(1, (int)Math.Floor(info.Height * pixelRatio)));
        return new Thumbnail(bitmap, displaySize);
    }

    internal sealed record Thumbnail(Bitmap Bitmap, Size DisplaySize);
}
