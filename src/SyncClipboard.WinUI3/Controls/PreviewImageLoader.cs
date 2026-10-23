using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SyncClipboard.Core;
using SyncClipboard.Core.Utilities.Images;
using Microsoft.Extensions.DependencyInjection;
using static SyncClipboard.WinUI3.Utilities.PreviewImageProvider;
using SyncClipboard.WinUI3.Utilities;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SyncClipboard.WinUI3.Controls;

/// <summary>
/// Loads bounded history images while their controls are loaded and individually visible.
/// The control returns its lease when hidden or recycled; the cache owns the bitmap references.
/// </summary>
public static class PreviewImageLoader
{
    public static readonly DependencyProperty FilePathProperty = DependencyProperty.RegisterAttached(
        "FilePath", typeof(string), typeof(PreviewImageLoader), new PropertyMetadata(null, OnOptionsChanged));
    public static readonly DependencyProperty IsThumbnailProperty = DependencyProperty.RegisterAttached(
        "IsThumbnail", typeof(bool), typeof(PreviewImageLoader), new PropertyMetadata(false, OnOptionsChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        nameof(State), typeof(State), typeof(PreviewImageLoader), new PropertyMetadata(null));

    public static string? GetFilePath(Image image) => (string?)image.GetValue(FilePathProperty);
    public static void SetFilePath(Image image, string? value) => image.SetValue(FilePathProperty, value);
    public static bool GetIsThumbnail(Image image) => (bool)image.GetValue(IsThumbnailProperty);
    public static void SetIsThumbnail(Image image, bool value) => image.SetValue(IsThumbnailProperty, value);

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
        private PreviewImageLease<DecodedImage>? _lease;
        private readonly PreviewImageCache<PreviewImageKey, DecodedImage> _cache;
        private bool _recycled;
        private (string Path, (uint Width, uint Height) Dimensions)? _cachedSize;

        internal event EventHandler? FileMissing;
        internal Task PendingLoad { get; private set; } = Task.CompletedTask;

        public State(Image image)
        {
            _image = image;
            _cache = AppCore.TryGetCurrent()?.Services.GetService<PreviewImageCache<PreviewImageKey, DecodedImage>>()
                ?? CreateCache(null);
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
            _lease?.Dispose();
            _lease = null;
            _image.Width = double.NaN;
            _image.Height = double.NaN;
            _image.MaxWidth = double.PositiveInfinity;
            _image.MaxHeight = double.PositiveInfinity;
            var path = GetFilePath(_image);
            if (_cachedSize?.Path != path)
                _cachedSize = null;
            if (!_image.IsLoaded || _recycled || string.IsNullOrEmpty(path))
                return;

            var thumbnail = GetIsThumbnail(_image);
            var scale = _image.XamlRoot?.RasterizationScale ?? 1;
            if (_cachedSize is { } cached)
            {
                try
                {
                    ApplySize(PreviewImageSize.Calculate(cached.Dimensions.Width, cached.Dimensions.Height, thumbnail, scale), thumbnail);
                }
                catch (InvalidDataException)
                {
                    return;
                }
            }

            var sizeOnly = _image.Visibility != Visibility.Visible;
            if (sizeOnly && (!thumbnail || _cachedSize is not null))
                return;

            _request = new CancellationTokenSource();
            PendingLoad = LoadAsync(path, thumbnail, scale, sizeOnly, _request.Token);
        }

        private void ApplySize(PreviewImageSize size, bool thumbnail)
        {
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
        }

        private async Task LoadAsync(string path, bool thumbnail, double scale, bool sizeOnly, CancellationToken token)
        {
            PreviewImageLease<DecodedImage>? lease = null;
            try
            {
                var key = await Task.Run(() => PreviewImageKey.Create(path, thumbnail ? 5120 : 2048,
                    thumbnail ? 200 : 2048, thumbnail ? scale : 1, thumbnail ? 5120 : 2048, sizeOnly, thumbnail), token);
                token.ThrowIfCancellationRequested();
                _cache.Invalidate(old => old.IsOlderVersionOf(key));
                lease = await _cache.AcquireAsync(key, token);
                var (bitmap, size, dimensions) = lease.Image;
                token.ThrowIfCancellationRequested();

                _cachedSize = (path, dimensions);
                ApplySize(size, thumbnail);
                _image.Stretch = thumbnail ? Stretch.UniformToFill : Stretch.Uniform;
                _lease = lease;
                _image.Source = bitmap;
                lease = null;
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
                    _cache.Invalidate(key => key.Path == Path.GetFullPath(path));
                    FileMissing?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                AppCore.TryGetCurrent()?.Logger.Write(nameof(PreviewImageLoader), $"Failed to load preview: {ex.Message}");
            }
            finally
            {
                lease?.Dispose();
            }
        }
    }
}
