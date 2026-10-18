using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Core.Utilities.History.HistoryExport;

namespace SyncClipboard.Core.ViewModels;

public sealed partial class HistoryExportViewModel(HistoryManager manager, HistoryExporter exporter) : ObservableObject, IDisposable
{
    private CancellationTokenSource? _cancellation;
    private IDisposable? _cleanupPause;
    private bool _disposed;
    private HistoryExportPlan? _plan;
    private Func<Task<string?>>? _pickDirectory;
    private IReadOnlyList<HistoryRecordKey>? _selected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeDirectoryCommand))]
    [NotifyPropertyChangedFor(nameof(ShowSetup), nameof(ShowStart))]
    private bool isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyPropertyChangedFor(nameof(ShowStart))]
    private bool ready;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSetup), nameof(ShowStart))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChangeDirectoryCommand))]
    private bool isFinished;

    public bool ShowSetup => !IsBusy && !IsFinished;
    public bool ShowStart => ShowSetup && Ready;
    public bool HasArchive => ArchivePath is not null;
    public bool HasReport => ReportPath is not null;
    public bool HasOutput => HasArchive || HasReport;

    [ObservableProperty]
    private string directory = string.Empty;

    [ObservableProperty]
    private string rangeDescription = string.Empty;

    [ObservableProperty]
    private string status = string.Empty;

    [ObservableProperty]
    private bool isPreparing;

    [ObservableProperty]
    private int processedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressMaximum))]
    private int totalCount;

    public int ProgressMaximum => Math.Max(1, TotalCount);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenFolderCommand))]
    [NotifyPropertyChangedFor(nameof(HasArchive), nameof(HasOutput))]
    private string? archivePath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenFolderCommand))]
    [NotifyPropertyChangedFor(nameof(HasReport), nameof(HasOutput))]
    private string? reportPath;

    public async Task InitializeAsync(string selectedDirectory, IReadOnlyList<HistoryRecordKey>? selected, Func<Task<string?>> pickDirectory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RangeDescription = selected is null ? Strings.HistoryExportAll : string.Format(Strings.HistoryExportSelectionCount, selected.Count);
        Directory = selectedDirectory;
        _selected = selected;
        _pickDirectory = pickDirectory;
        IsPreparing = true;
        ProcessedCount = 0;
        TotalCount = 0;
        IsBusy = true;
        IsFinished = false;
        Ready = false;
        Status = Strings.HistoryExportEstimating;
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        try
        {
            _plan = null;
            _cleanupPause ??= await manager.PauseCleanupAsync(cancellation.Token);
            _plan = await Task.Run(async () =>
            {
                var snapshot = await manager.GetExportSnapshotAsync(selected, cancellation.Token);
                return await exporter.EstimateAsync(snapshot, selected, cancellation.Token);
            }, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            TotalCount = _plan.Items.Count;
            var totalCount = _plan.Items.Count + _plan.Skipped.Count;
            Ready = totalCount > 0;
            Status = Ready
                ? string.Format(Strings.HistoryExportEstimate, totalCount, FormatSize(_plan.EstimatedBytes))
                : Strings.HistoryExportEmpty;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Status = Strings.HistoryExportCanceled;
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.HistoryExportFailed, ex.Message);
        }
        finally
        {
            _cancellation = null;
            IsBusy = false;
            IsPreparing = false;
            if (!Ready || _disposed)
                ReleaseCleanupPause();
        }
    }

    private bool CanStart() => ShowStart;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_plan is null)
            return;
        Ready = false;
        IsBusy = true;
        ProcessedCount = 0;
        TotalCount = _plan.Items.Count;
        Status = string.Format(Strings.HistoryExportProgress, ProcessedCount, TotalCount, FormatSize(0));
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var progress = new Progress<HistoryExportProgress>(value =>
        {
            if (IsBusy)
            {
                ProcessedCount = value.Processed;
                TotalCount = value.Total;
                Status = string.Format(Strings.HistoryExportProgress, ProcessedCount, TotalCount, FormatSize(value.WrittenBytes));
            }
        });
        try
        {
            var result = await Task.Run(() => exporter.ExportAsync(_plan, Directory, progress, cancellation.Token));
            ShowResult(result);
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.HistoryExportFailed, ex.Message);
        }
        finally
        {
            _cancellation = null;
            IsFinished = true;
            IsBusy = false;
            ReleaseCleanupPause();
        }
    }

    private void ShowResult(HistoryExportResult result)
    {
        // Ignore progress queued by the background writer after the final result.
        IsFinished = true;
        IsBusy = false;
        if (!result.Canceled && result.Error is null)
            ProcessedCount = TotalCount;
        ArchivePath = result.ArchivePath;
        ReportPath = result.ReportPath;
        Status = HistoryExportReportWriter.FormatResult(result);
        if (result.Canceled)
            Status += "\n" + Strings.HistoryExportCanceled;
        if (result.Error is not null)
            Status += "\n" + string.Format(Strings.HistoryExportFailed, result.Error);
        if (result.ArchivePath is null)
            Status += "\n" + Strings.HistoryExportNoArchive;
        if (result.ReportError is not null)
        {
            Status += "\n" + string.Format(Strings.HistoryExportReportFailed, result.ReportError);
            Status += "\n" + string.Join('\n', result.Skipped.Select(item =>
                $"{item.ProfileId}: {HistoryExportReportWriter.Describe(item.Reason)} {item.Detail}"));
        }
    }

    private bool CanChangeDirectory() => ShowSetup;

    [RelayCommand(CanExecute = nameof(CanChangeDirectory))]
    private async Task ChangeDirectoryAsync()
    {
        if (_pickDirectory is null)
            return;
        IsBusy = true;
        try
        {
            if (await _pickDirectory() is { } path)
                await InitializeAsync(path, _selected, _pickDirectory);
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.HistoryExportFailed, ex.Message);
        }
        finally
        {
            IsBusy = false;
            if (_disposed)
                ReleaseCleanupPause();
        }
    }

    public void Cancel() => _cancellation?.Cancel();

    private bool CanOpenFolder() => HasOutput;

    [RelayCommand(CanExecute = nameof(CanOpenFolder))]
    private void OpenFolder()
    {
        try
        {
            Sys.OpenFolderInFileManager(Directory);
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }

    private static string FormatSize(long size) => size >= 1024 * 1024
        ? $"{size / (1024d * 1024):0.##} MiB" : $"{size / 1024d:0.##} KiB";

    private void ReleaseCleanupPause() => Interlocked.Exchange(ref _cleanupPause, null)?.Dispose();

    public void Dispose()
    {
        _disposed = true;
        Cancel();
        // Active operations release only after their file I/O has stopped.
        if (!IsBusy)
            ReleaseCleanupPause();
    }
}
