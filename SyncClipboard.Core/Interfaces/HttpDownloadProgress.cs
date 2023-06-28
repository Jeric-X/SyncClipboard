namespace SyncClipboard.Core.Interfaces
{
    public struct HttpDownloadProgress
    {
        public ulong BytesReceived { get; set; }

        public ulong? TotalBytesToReceive { get; set; }
        public bool End { get; set; }
    }
}