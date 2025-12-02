using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.History;

namespace SyncClipboard.Core.ViewModels.Sub;

public partial class HistoryRecordVM(HistoryRecord record) : ObservableObject
{
    private readonly int id = record.ID;

    [ObservableProperty]
    private string text = record.Text;
    public ProfileType Type { get; set; } = record.Type;
    [ObservableProperty]
    private string[] filePath = RestoreFilePath(record.FilePath, record.Type, record.Hash);
    public string Hash { get; set; } = record.Hash;
    public long Size { get; set; } = record.Size;
    [ObservableProperty]
    private DateTime timestamp = record.Timestamp;
    [ObservableProperty]
    private bool stared = record.Stared;
    [ObservableProperty]
    private bool pinned = record.Pinned;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUploadButton))]
    [NotifyPropertyChangedFor(nameof(ShowUploadProgress))]
    private SyncStatus syncState = record.SyncStatus == HistorySyncStatus.LocalOnly ? SyncStatus.LocalOnly :
        record.IsLocalFileReady ? SyncStatus.Synced : SyncStatus.ServerOnly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDownloadButton))]
    [NotifyPropertyChangedFor(nameof(ShowDownloadProgress))]
    private bool isDownloading = false;

    [ObservableProperty]
    private double downloadProgress = 0; // 0.0 - 100.0 百分比

    [ObservableProperty]
    private bool isDownloadPending = false;

    // 上传相关属性
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUploadButton))]
    [NotifyPropertyChangedFor(nameof(ShowUploadProgress))]
    private bool isUploading = false;

    [ObservableProperty]
    private double uploadProgress = 0; // 0.0 - 100.0 百分比

    [ObservableProperty]
    private bool isUploadPending = false;

    public bool ShowUploadButton => SyncState == SyncStatus.LocalOnly && !IsUploading;
    public bool ShowUploadProgress => SyncState == SyncStatus.LocalOnly && IsUploading;

    [ObservableProperty]
    private bool hasError = false;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDownloadButton))]
    [NotifyPropertyChangedFor(nameof(ShowDownloadProgress))]
    private bool isLocalFileReady = record.IsLocalFileReady;
    public bool ShowDownloadButton => !IsLocalFileReady && !IsDownloading;
    public bool ShowDownloadProgress => !IsLocalFileReady && IsDownloading;

    private Progress<HttpDownloadProgress>? downloadProgressReporter = null;
    private Progress<HttpDownloadProgress>? uploadProgressReporter = null;

    internal void UnsubscribeDownloadProgress()
    {
        if (downloadProgressReporter != null)
        {
            downloadProgressReporter.ProgressChanged -= OnDownloadProgressChanged;
            downloadProgressReporter = null;
        }
    }

    internal void UnsubscribeUploadProgress()
    {
        if (uploadProgressReporter != null)
        {
            uploadProgressReporter.ProgressChanged -= OnUploadProgressChanged;
            uploadProgressReporter = null;
        }
    }

    internal void SubscribeDownloadProgress(Progress<HttpDownloadProgress> progress)
    {
        downloadProgressReporter = progress;
        downloadProgressReporter.ProgressChanged += OnDownloadProgressChanged;
    }

    internal void SubscribeUploadProgress(Progress<HttpDownloadProgress> progress)
    {
        uploadProgressReporter = progress;
        uploadProgressReporter.ProgressChanged += OnUploadProgressChanged;
    }

    private void OnDownloadProgressChanged(object? sender, HttpDownloadProgress p)
    {
        double progressValue;
        if (p.TotalBytesToReceive.HasValue && p.TotalBytesToReceive.Value > 0)
        {
            progressValue = (double)p.BytesReceived / p.TotalBytesToReceive.Value * 100.0;
        }
        else
        {
            progressValue = 0;
        }
        DownloadProgress = progressValue;
    }

    private void OnUploadProgressChanged(object? sender, HttpDownloadProgress p)
    {
        double progressValue;
        if (p.TotalBytesToReceive.HasValue && p.TotalBytesToReceive.Value > 0)
        {
            progressValue = (double)p.BytesReceived / p.TotalBytesToReceive.Value * 100.0;
        }
        else
        {
            progressValue = 0;
        }
        UploadProgress = progressValue;
    }

    internal void UpdateFromTask(TransferTask task)
    {
        if (task.Type == TransferType.Download)
        {
            UnsubscribeDownloadProgress();
        }
        else if (task.Type == TransferType.Upload)
        {
            UnsubscribeUploadProgress();
        }

        var isPending = task.Status == TransferTaskStatus.Pending ||
                        task.Status == TransferTaskStatus.WaitForRetry;
        var isWorking = task.Status == TransferTaskStatus.Running || isPending;
        if (task.Type == TransferType.Download)
        {
            IsDownloading = isWorking;
            DownloadProgress = task.Progress;
            IsDownloadPending = isPending;
        }
        else
        {
            IsUploading = isWorking;
            UploadProgress = task.Progress;
            IsUploadPending = isPending;
        }

        HasError = false;
        if (task.Status == TransferTaskStatus.Failed || task.Status == TransferTaskStatus.WaitForRetry)
        {
            HasError = true;
            ErrorMessage = task.ErrorMessage ?? string.Empty;
        }
        else if (task.Status == TransferTaskStatus.Completed)
        {
            SyncState = SyncStatus.Synced;
        }

        if (task.Status == TransferTaskStatus.Running)
        {
            if (task.Type == TransferType.Download)
            {
                SubscribeDownloadProgress(task.ProgressReporter);
            }
            else
            {
                SubscribeUploadProgress(task.ProgressReporter);
            }
        }
    }

    public HistoryRecord ToHistoryRecord()
    {
        return new HistoryRecord
        {
            ID = id,
            Text = Text,
            Type = Type,
            FilePath = FilePath,
            Hash = Hash,
            Timestamp = Timestamp,
            Stared = Stared,
            Pinned = Pinned,
            IsLocalFileReady = IsLocalFileReady,
            Size = Size
        };
    }

    public void Update(HistoryRecordVM record)
    {
        Text = record.Text;
        FilePath = RestoreFilePath(record.FilePath, record.Type, record.Hash);
        Size = record.Size;
        Timestamp = record.Timestamp;
        Stared = record.Stared;
        Pinned = record.Pinned;
        SyncState = record.SyncState;
        IsLocalFileReady = record.IsLocalFileReady;
    }

    public override bool Equals(object? obj)
    {
        if (obj is not HistoryRecordVM other)
        {
            return false;
        }

        if (Type != other.Type)
        {
            return false;
        }

        return string.Equals(Hash, other.Hash, StringComparison.OrdinalIgnoreCase);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Type, Hash);
    }

    public static bool operator ==(HistoryRecordVM? left, HistoryRecordVM? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;
        return left.Equals(right);
    }

    public static bool operator !=(HistoryRecordVM? left, HistoryRecordVM? right)
    {
        return !(left == right);
    }

    private static string[] RestoreFilePath(string[]? persistentPaths, ProfileType type, string hash)
    {
        var profileEnv = AppCore.Current.Services.GetRequiredService<IProfileEnv>();
        if (persistentPaths is null || persistentPaths.Length == 0)
        {
            return [];
        }

        var workingDir = Profile.GetWorkingDir(profileEnv.GetHistoryPersistentDir(), type, hash);
        var restoredPaths = new string[persistentPaths.Length];
        for (int i = 0; i < persistentPaths.Length; i++)
        {
            restoredPaths[i] = Profile.GetFullPath(workingDir, persistentPaths[i]);
        }

        return restoredPaths;
    }
}