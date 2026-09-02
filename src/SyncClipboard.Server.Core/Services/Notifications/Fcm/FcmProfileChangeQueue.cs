using System.Threading.Channels;

namespace SyncClipboard.Server.Core.Services.Notifications.Fcm;

public sealed record FcmProfileChangeHint(
    string ProfileHash,
    string? OriginDeviceId);

public interface IFcmProfileChangeQueue
{
    bool TryEnqueue(FcmProfileChangeHint hint);

    ValueTask<FcmProfileChangeHint> DequeueAsync(
        CancellationToken cancellationToken = default);
}

public sealed class FcmProfileChangeQueue : IFcmProfileChangeQueue
{
    private readonly Channel<FcmProfileChangeHint> _channel =
        Channel.CreateBounded<FcmProfileChangeHint>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    public bool TryEnqueue(FcmProfileChangeHint hint)
    {
        return _channel.Writer.TryWrite(hint);
    }

    public ValueTask<FcmProfileChangeHint> DequeueAsync(
        CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAsync(cancellationToken);
    }
}
