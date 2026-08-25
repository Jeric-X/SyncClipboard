using SyncClipboard.Core.Commons;

namespace SyncClipboard.Core.Options;

public sealed class LoggerOption
{
    private volatile bool _flushImmediately = true;

    public string Path { get; init; } = Env.LogFolder;

    public bool FlushImmediately
    {
        get => _flushImmediately;
        set => _flushImmediately = value;
    }
}
