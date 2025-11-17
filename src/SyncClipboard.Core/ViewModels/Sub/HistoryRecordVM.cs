using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.ViewModels.Sub;

public partial class HistoryRecordVM(HistoryRecord record) : ObservableObject
{
    private readonly int id = record.ID;

    [ObservableProperty]
    private string text = record.Text;
    public ProfileType Type { get; set; } = record.Type;
    [ObservableProperty]
    private string[] filePath = record.FilePath;
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

    // 上传相关属性
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUploadButton))]
    [NotifyPropertyChangedFor(nameof(ShowUploadProgress))]
    private bool isUploading = false;

    [ObservableProperty]
    private double uploadProgress = 0; // 0.0 - 100.0 百分比

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
        FilePath = record.FilePath;
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

        return Hash == other.Hash;
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

    private static double ComputePercent(HttpDownloadProgress p, ulong? fallbackTotal)
    {
        try
        {
            ulong total = 0;
            if (p.TotalBytesToReceive.HasValue && p.TotalBytesToReceive.Value > 0)
            {
                total = p.TotalBytesToReceive.Value;
            }
            else if (fallbackTotal.HasValue && fallbackTotal.Value > 0)
            {
                total = fallbackTotal.Value;
            }

            return total > 0
                ? Math.Clamp((double)p.BytesReceived / total * 100.0, 0.0, 100.0)
                : Math.Clamp((double)p.BytesReceived / 1000000.0, 0.0, 100.0);
        }
        catch
        {
            return 0.0;
        }
    }

    public IProgress<HttpDownloadProgress> CreateDownloadProgress()
    {
        return new Progress<HttpDownloadProgress>(p =>
        {
            DownloadProgress = ComputePercent(p, (ulong)Size);
        });
    }

    public IProgress<HttpDownloadProgress> CreateUploadProgress(string? transferFilePath)
    {
        ulong? fallbackTotal = null;
        try
        {
            if (!string.IsNullOrEmpty(transferFilePath) && File.Exists(transferFilePath))
            {
                var fi = new FileInfo(transferFilePath);
                fallbackTotal = (ulong)fi.Length;
            }
        }
        catch { }
        return new Progress<HttpDownloadProgress>(p =>
        {
            UploadProgress = ComputePercent(p, fallbackTotal);
        });
    }
}