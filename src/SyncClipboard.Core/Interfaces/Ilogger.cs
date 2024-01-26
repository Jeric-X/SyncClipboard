namespace SyncClipboard.Core.Interfaces
{
    public interface ILogger
    {
        void Write(string? tag, string str);
        void Write(string str);
        void Flush();
    }
}