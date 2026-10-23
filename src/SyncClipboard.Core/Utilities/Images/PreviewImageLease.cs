namespace SyncClipboard.Core.Utilities.Images;

/// <summary>A single consumer's reference to a cached preview. Disposing it never disposes an image still in use.</summary>
public sealed class PreviewImageLease<T> : IDisposable where T : class
{
    private Action? _release;
    private T? _image;

    internal PreviewImageLease(T image, Action release)
    {
        _image = image;
        _release = release;
    }

    public T Image => _image ?? throw new ObjectDisposedException(nameof(PreviewImageLease<>));

    public void Dispose()
    {
        var release = Interlocked.Exchange(ref _release, null);
        _image = null;
        release?.Invoke();
    }
}
