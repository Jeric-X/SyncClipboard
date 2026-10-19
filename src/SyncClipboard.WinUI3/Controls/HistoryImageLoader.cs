using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SyncClipboard.Core;
using SyncClipboard.WinUI3.Utilities;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;

namespace SyncClipboard.WinUI3.Controls;

/// <summary>
/// Loads bounded history images while their controls are loaded and individually visible.
/// WinUI owns the bitmap resources; clearing Source releases the control's reference.
/// </summary>
public static class HistoryImageLoader
{
    public static readonly DependencyProperty FilePathProperty = DependencyProperty.RegisterAttached(
        "FilePath", typeof(string), typeof(HistoryImageLoader), new PropertyMetadata(null, OnOptionsChanged));
    public static readonly DependencyProperty IsThumbnailProperty = DependencyProperty.RegisterAttached(
        "IsThumbnail", typeof(bool), typeof(HistoryImageLoader), new PropertyMetadata(false, OnOptionsChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        nameof(State), typeof(State), typeof(HistoryImageLoader), new PropertyMetadata(null));

    public static string? GetFilePath(Image image) => (string?)image.GetValue(FilePathProperty);
    public static void SetFilePath(Image image, string? value) => image.SetValue(FilePathProperty, value);
    public static bool GetIsThumbnail(Image image) => (bool)image.GetValue(IsThumbnailProperty);
    public static void SetIsThumbnail(Image image, bool value) => image.SetValue(IsThumbnailProperty, value);

    private static void OnOptionsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is Image image)
        {
            GetState(image).Update(resetSize: true);
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
        private static readonly SemaphoreSlim HeaderSlots = new(2);
        private readonly Image _image;
        private CancellationTokenSource? _request;
        private bool _loaded;

        internal event EventHandler? FileMissing;
        internal Task PendingLoad { get; private set; } = Task.CompletedTask;

        public State(Image image)
        {
            _image = image;
            image.Loaded += OnLoaded;
            image.Unloaded += OnUnloaded;
            image.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, OnVisibilityChanged);
            _loaded = image.IsLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs args)
        {
            _loaded = true;
            Update();
        }

        private void OnUnloaded(object sender, RoutedEventArgs args)
        {
            _loaded = false;
            Update();
        }

        private void OnVisibilityChanged(DependencyObject sender, DependencyProperty property) => Update();

        internal void Update(bool resetSize = false)
        {
            _request?.Cancel();
            _request?.Dispose();
            _request = null;
            _image.Source = null;
            // Releasing pixels must not collapse rows and change the scroll extent.
            // Only a different file or sizing policy invalidates the previous layout.
            if (resetSize)
            {
                _image.Width = double.NaN;
                _image.Height = double.NaN;
                _image.MaxWidth = double.PositiveInfinity;
                _image.MaxHeight = double.PositiveInfinity;
            }

            if (!_loaded || _image.Visibility != Visibility.Visible || string.IsNullOrEmpty(GetFilePath(_image)))
                return;

            _request = new CancellationTokenSource();
            PendingLoad = LoadAsync(GetFilePath(_image)!, GetIsThumbnail(_image),
                _image.XamlRoot?.RasterizationScale ?? 1, _request.Token);
        }

        private async Task LoadAsync(string path, bool thumbnail, double scale, CancellationToken token)
        {
            try
            {
                var size = await ReadSizeAsync(path, thumbnail, scale, token);
                token.ThrowIfCancellationRequested();
                var bitmap = new BitmapImage
                {
                    DecodePixelType = DecodePixelType.Physical,
                    DecodePixelWidth = size.PixelWidth,
                    DecodePixelHeight = size.PixelHeight,
                };

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
                _image.Stretch = Stretch.Uniform;
                _image.Source = bitmap;
                bitmap.UriSource = new Uri(path);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Hidden, unloaded, or replaced images must not restore a stale source.
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
                AppCore.TryGetCurrent()?.Logger.Write(nameof(HistoryImageLoader), $"Failed to load preview: {ex.Message}");
            }
        }

        private static async Task<HistoryImageSize> ReadSizeAsync(string path, bool thumbnail, double scale, CancellationToken token)
        {
            await HeaderSlots.WaitAsync(token).ConfigureAwait(false);
            try
            {
                return await Task.Run(async () =>
                {
                    token.ThrowIfCancellationRequested();
                    using var stream = File.OpenRead(path);
                    using var randomAccessStream = stream.AsRandomAccessStream();
                    var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
                    token.ThrowIfCancellationRequested();
                    // A failed header is not retried with an unbounded full decode.
                    return HistoryImageSize.Calculate(decoder.OrientedPixelWidth, decoder.OrientedPixelHeight, thumbnail, scale);
                }, token).ConfigureAwait(false);
            }
            finally
            {
                HeaderSlots.Release();
            }
        }
    }
}
