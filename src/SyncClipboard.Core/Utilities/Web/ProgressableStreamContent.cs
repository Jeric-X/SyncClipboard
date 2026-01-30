using SyncClipboard.Core.Models;
using System.Net;

namespace SyncClipboard.Core.Utilities.Web;

public sealed class ProgressableStreamContent : HttpContent
{
    private const int DefaultBufferSize = 81920; // 80KB .NET default
    private readonly Stream _stream;
    private readonly IProgress<HttpDownloadProgress> _progress;
    private readonly CancellationToken _ct;
    private readonly long? _totalLength;

    public ProgressableStreamContent(Stream stream, IProgress<HttpDownloadProgress> progress, CancellationToken ct)
    {
        _stream = stream;
        _progress = progress;
        _ct = ct;
        if (stream.CanSeek)
        {
            _totalLength = stream.Length;
        }
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        var buffer = new byte[DefaultBufferSize];
        long totalBytesRead = 0;
        int bytesRead;

        _progress.Report(new HttpDownloadProgress
        {
            BytesReceived = 0,
            TotalBytesToReceive = (ulong?)_totalLength
        });

        while ((bytesRead = await _stream.ReadAsync(buffer, _ct)) > 0)
        {
            await stream.WriteAsync(buffer.AsMemory(0, bytesRead), _ct);
            totalBytesRead += bytesRead;
            _progress.Report(new HttpDownloadProgress
            {
                BytesReceived = (ulong)totalBytesRead,
                TotalBytesToReceive = (ulong?)_totalLength
            });
        }

        _progress.Report(new HttpDownloadProgress
        {
            BytesReceived = (ulong)totalBytesRead,
            TotalBytesToReceive = (ulong?)_totalLength,
            End = true
        });
    }

    protected override bool TryComputeLength(out long length)
    {
        if (_totalLength is not null)
        {
            length = _totalLength.Value;
            return true;
        }
        length = -1;
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _stream.Dispose();
        }
    }
}
