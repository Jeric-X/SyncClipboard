using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SyncClipboard.Core;
using SyncClipboard.Core.Models;
using SyncClipboard.WinUI3.Utilities;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace SyncClipboard.WinUI3.Controls;

/// <summary>
/// Loads bounded history images while their controls are loaded and individually visible.
/// WinUI owns the bitmap resources; clearing Source releases the control's reference.
/// </summary>
public static class PreviewImageLoader
{
    private static readonly SemaphoreSlim DecodeSlots = new(2);
    public static readonly DependencyProperty FilePathProperty = DependencyProperty.RegisterAttached(
        "FilePath", typeof(string), typeof(PreviewImageLoader), new PropertyMetadata(null, OnOptionsChanged));
    public static readonly DependencyProperty IsThumbnailProperty = DependencyProperty.RegisterAttached(
        "IsThumbnail", typeof(bool), typeof(PreviewImageLoader), new PropertyMetadata(false, OnOptionsChanged));
    public static readonly DependencyProperty MetadataProperty = DependencyProperty.RegisterAttached(
        "Metadata", typeof(HistoryImageMetadata), typeof(PreviewImageLoader), new PropertyMetadata(null, OnOptionsChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        nameof(State), typeof(State), typeof(PreviewImageLoader), new PropertyMetadata(null));

    public static string? GetFilePath(Image image) => (string?)image.GetValue(FilePathProperty);
    public static void SetFilePath(Image image, string? value) => image.SetValue(FilePathProperty, value);
    public static bool GetIsThumbnail(Image image) => (bool)image.GetValue(IsThumbnailProperty);
    public static void SetIsThumbnail(Image image, bool value) => image.SetValue(IsThumbnailProperty, value);

    public static HistoryImageMetadata? GetMetadata(Image image) => (HistoryImageMetadata?)image.GetValue(MetadataProperty);
    public static void SetMetadata(Image image, HistoryImageMetadata? value) => image.SetValue(MetadataProperty, value);

    internal static void SetContainerRecycled(DependencyObject container, bool recycled)
    {
        if (container.GetValue(StateProperty) is State state)
        {
            state.SetRecycled(recycled);
        }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(container); i++)
        {
            SetContainerRecycled(VisualTreeHelper.GetChild(container, i), recycled);
        }
    }

    private static void OnOptionsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is Image image)
        {
            GetState(image).Update();
        }
    }

    internal static State GetState(Image image)
    {
        if (image.GetValue(StateProperty) is not State state)
        {
            state = new State(image);
            image.SetValue(StateProperty, state);
        }
        return state;
    }

    internal sealed class State
    {
        private readonly Image _image;
        private CancellationTokenSource? _request;
        private bool _recycled;

        internal event EventHandler? FileMissing;
        internal Task PendingLoad { get; private set; } = Task.CompletedTask;

        public State(Image image)
        {
            _image = image;
            image.Loaded += OnLoadedChanged;
            image.Unloaded += OnLoadedChanged;
            image.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, OnVisibilityChanged);
        }

        private void OnLoadedChanged(object sender, RoutedEventArgs args) => Update();

        private void OnVisibilityChanged(DependencyObject sender, DependencyProperty property) => Update();

        internal void SetRecycled(bool recycled)
        {
            if (_recycled == recycled)
                return;

            _recycled = recycled;
            Update();
        }

        internal void Update()
        {
            _request?.Cancel();
            _request?.Dispose();
            _request = null;
            _image.Source = null;
            _image.Width = double.NaN;
            _image.Height = double.NaN;
            _image.MaxWidth = double.PositiveInfinity;
            _image.MaxHeight = double.PositiveInfinity;
            var path = GetFilePath(_image);
            if (!_image.IsLoaded || _recycled || string.IsNullOrEmpty(path))
                return;

            var thumbnail = GetIsThumbnail(_image);
            var scale = _image.XamlRoot?.RasterizationScale ?? 1;
            // History records prepare metadata off the UI thread before entering the list.
            if (thumbnail && GetMetadata(_image) is { Width: > 0, Height: > 0 } metadata && metadata.FilePath == path)
            {
                var width = metadata.Rotated ? metadata.Height : metadata.Width;
                var height = metadata.Rotated ? metadata.Width : metadata.Height;
                var ratio = Math.Min(1, Math.Min(5120.0 / width, 200.0 / height));
                _image.Width = width * ratio;
                _image.Height = height * ratio;
            }

            if (_image.Visibility != Visibility.Visible)
                return;

            _request = new CancellationTokenSource();
            PendingLoad = LoadAsync(path, thumbnail, scale, _request.Token);
        }

        private async Task LoadAsync(string path, bool thumbnail, double scale, CancellationToken token)
        {
            try
            {
                var (bitmap, size) = await DecodeAsync(path, thumbnail, scale, token);
                token.ThrowIfCancellationRequested();

                if (thumbnail)
                {
                    _image.Width = size.Width;
                    _image.Height = size.Height;
                }
                else
                {
                    // Uniform alone also enlarges small images to fill the panel.
                    _image.MaxWidth = size.Width;
                    _image.MaxHeight = size.Height;
                }
                _image.Stretch = thumbnail ? Stretch.UniformToFill : Stretch.Uniform;
                _image.Source = bitmap;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Hidden, unloaded, or replaced images must not restore a stale source.
                return;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                if (!token.IsCancellationRequested)
                {
                    FileMissing?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                AppCore.TryGetCurrent()?.Logger.Write(nameof(PreviewImageLoader), $"Failed to load preview: {ex.Message}");
            }
        }
    }

    internal static async Task<(BitmapImage Bitmap, PreviewImageSize Size)> DecodeAsync(
        string path, bool thumbnail, double scale, CancellationToken token,
        Func<IRandomAccessStream, Task<(uint Width, uint Height)>>? readHeader = null)
    {
        await DecodeSlots.WaitAsync(token);
        try
        {
            using var file = File.OpenRead(path);
            using var stream = file.AsRandomAccessStream();
            (uint Width, uint Height) dimensions = default;
            try
            {
                dimensions = await (readHeader ?? ReadDimensionsAsync)(stream);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                dimensions = default;
            }

            token.ThrowIfCancellationRequested();
            stream.Seek(0);
            var bitmap = new BitmapImage { DecodePixelType = DecodePixelType.Physical };
            var fullDecode = dimensions.Width == 0 || dimensions.Height == 0;
            if (fullDecode)
            {
                if (stream.Size >= 10UL * 1024 * 1024)
                    throw new InvalidDataException("Image requires full decoding but is not smaller than 10 MiB.");

                await bitmap.SetSourceAsync(stream);
                token.ThrowIfCancellationRequested();
                dimensions = ((uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight);
                stream.Seek(0);
            }

            var size = PreviewImageSize.Calculate(dimensions.Width, dimensions.Height, thumbnail, scale, fullDecode);
            bitmap.DecodePixelWidth = size.PixelWidth;
            bitmap.DecodePixelHeight = size.PixelHeight;
            await bitmap.SetSourceAsync(stream);
            token.ThrowIfCancellationRequested();
            return (bitmap, size);
        }
        finally
        {
            // Native decoding cannot be interrupted safely. Keep its slot until it finishes.
            DecodeSlots.Release();
        }
    }

    private static Task<(uint Width, uint Height)> ReadDimensionsAsync(IRandomAccessStream stream) => Task.Run(async () =>
    {
        var decoder = await BitmapDecoder.CreateAsync(stream);
        return (decoder.OrientedPixelWidth, decoder.OrientedPixelHeight);
    });
}
