using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Utilities.History;

public enum TransferType
{
    Upload,
    Download
}

public enum TransferTaskStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled,
    WaitForRetry
}

public class TransferTask
{
    public string ProfileId { get; init; } = string.Empty;
    public string TaskId => GetTaskId(Type, ProfileId);
    public TransferType Type { get; init; }
    public TransferTaskStatus Status { get; set; }
    public double Progress { get; set; }
    public bool IsImmediateTask { get; set; }
    public Progress<HttpDownloadProgress> ProgressReporter { get; }
    public IProgress<HttpDownloadProgress>? ExternalProgressReporter { get; set; }
    public long? TotalBytes { get; set; }
    public long? TransferredBytes { get; set; }
    public string? ErrorMessage { get; set; }
    public int FailureCount { get; set; }
    public DateTime CreatedTime { get; init; }
    public DateTime? StartedTime { get; set; }
    public DateTime? CompletedTime { get; set; }

    public Profile Profile { get; init; } = null!;
    public CancellationTokenSource CancellationSource { get; set; } = null!;

    // 任务完成信号
    public TaskCompletionSource<TransferTaskStatus> CompletionSource { get; init; } = new();

    public static string GetTaskId(TransferType type, string profileId)
    {
        return $"{type}-{profileId}";
    }

    public TransferTask()
    {
        ProgressReporter = new Progress<HttpDownloadProgress>(UpdateProgress);
    }

    /// <summary>
    /// 更新任务进度
    /// </summary>
    private void UpdateProgress(HttpDownloadProgress p)
    {
        if (p.TotalBytesToReceive.HasValue && p.TotalBytesToReceive.Value > 0)
        {
            Progress = (double)p.BytesReceived / p.TotalBytesToReceive.Value * 100.0;
        }
        else
        {
            Progress = 0;
        }

        TotalBytes = (long?)p.TotalBytesToReceive;
        TransferredBytes = (long)p.BytesReceived;

        ExternalProgressReporter?.Report(p);
    }
}
