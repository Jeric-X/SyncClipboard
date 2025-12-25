using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.History;

namespace SyncClipboard.Core.ViewModels.Sub;

public partial class HistoryRecordVM(HistoryRecord record) : ObservableObject
{
    private HistoryRecordVM() : this(new HistoryRecord())
    {
    }

    private readonly int id = record.ID;
    private readonly IThreadDispatcher _threadDispatcher = AppCore.Current.Services.GetRequiredService<IThreadDispatcher>();

    [ObservableProperty]
    private string text = record.Text;
    public ProfileType Type { get; set; } = record.Type;
    [ObservableProperty]
    private string[] filePath = RestoreFilePath(record.FilePath, record.Type, record.Hash);
    partial void OnFilePathChanged(string[]? oldValue, string[] newValue) => UpdatePreviewImage();

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
    [NotifyPropertyChangedFor(nameof(ShowDownloadButton))]
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

    public bool ShowUploadButton => SyncState == SyncStatus.LocalOnly && IsLocalFileReady && !IsUploading;
    public bool ShowUploadProgress => IsUploading;

    [ObservableProperty]
    private bool hasError = false;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDownloadButton))]
    [NotifyPropertyChangedFor(nameof(ShowUploadButton))]
    private bool isLocalFileReady = record.IsLocalFileReady;
    partial void OnIsLocalFileReadyChanged(bool oldValue, bool newValue) => UpdatePreviewImage();

    public bool ShowDownloadButton => !IsLocalFileReady && SyncState != SyncStatus.LocalOnly && !IsDownloading;
    public bool ShowDownloadProgress => IsDownloading;

    [ObservableProperty]
    public string? previewImage;

    private void UpdatePreviewImage()
    {
        PreviewImage = IsLocalFileReady && FilePath.Length > 0 ? FilePath[0] : null;
    }

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
        double progressValue = 0;
        if (p.TotalBytesToReceive.HasValue && p.TotalBytesToReceive.Value > 0)
        {
            progressValue = (double)p.BytesReceived / p.TotalBytesToReceive.Value * 100.0;
        }
        _threadDispatcher.RunOnMainThreadAsync(() =>
        {
            if (progressValue != 0)
            {
                DownloadProgress = progressValue;
                IsDownloadPending = false;
            }
        });
    }

    private void OnUploadProgressChanged(object? sender, HttpDownloadProgress p)
    {
        double progressValue = 0;
        if (p.TotalBytesToReceive.HasValue && p.TotalBytesToReceive.Value > 0)
        {
            progressValue = (double)p.BytesReceived / p.TotalBytesToReceive.Value * 100.0;
        }
        _threadDispatcher.RunOnMainThreadAsync(() =>
        {
            if (progressValue != 0)
            {
                UploadProgress = progressValue;
                IsUploadPending = false;
            }
        });
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
            IsDownloadPending = isWorking;
            DownloadProgress = task.Progress;
        }
        else
        {
            IsUploading = isWorking;
            IsUploadPending = isWorking;
            UploadProgress = task.Progress;
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
        Type = record.Type;
        FilePath = RestoreFilePath(record.FilePath, record.Type, record.Hash);
        Hash = record.Hash;
        Size = record.Size;
        Timestamp = record.Timestamp;
        Stared = record.Stared;
        Pinned = record.Pinned;
        SyncState = record.SyncState;
        IsDownloading = record.IsDownloading;
        DownloadProgress = record.DownloadProgress;
        IsDownloadPending = record.IsDownloadPending;
        IsUploading = record.IsUploading;
        UploadProgress = record.UploadProgress;
        IsUploadPending = record.IsUploadPending;
        HasError = record.HasError;
        ErrorMessage = record.ErrorMessage;
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

    /// <summary>
    /// 创建当前对象的深拷贝
    /// </summary>
    public HistoryRecordVM DeepCopy()
    {
        var copy = new HistoryRecordVM
        {
            Text = this.Text,
            Type = this.Type,
            FilePath = this.FilePath,
            Hash = this.Hash,
            Size = this.Size,
            Timestamp = this.Timestamp,
            Stared = this.Stared,
            Pinned = this.Pinned,
            SyncState = this.SyncState,
            IsDownloading = this.IsDownloading,
            DownloadProgress = this.DownloadProgress,
            IsDownloadPending = this.IsDownloadPending,
            IsUploading = this.IsUploading,
            UploadProgress = this.UploadProgress,
            IsUploadPending = this.IsUploadPending,
            HasError = this.HasError,
            ErrorMessage = this.ErrorMessage,
            IsLocalFileReady = this.IsLocalFileReady
        };

        return copy;
    }
}