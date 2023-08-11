namespace SyncClipboard.Core.Models
{
    public struct HttpDownloadProgress
    {
        public ulong BytesReceived { get; set; }

        public ulong? TotalBytesToReceive { get; set; }
        public bool End { get; set; }
    }
}