namespace SyncClipboard.Core.Interfaces
{
    public interface ILogger
    {
        void Write(string? tag, string str);
        void Write(string str);
        Task WriteAsync(string? tag, string str);
        Task WriteAsync(string str);
        void Flush();
    }
}