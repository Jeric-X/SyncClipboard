using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.I18n;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.History.HistoryImport;

namespace SyncClipboard.Core.ViewModels;

public sealed partial class HistoryImportViewModel(HistoryImporter importer) : ObservableObject, IDisposable
{
    private HistoryImportPlan? _plan;
    private CancellationTokenSource? _cancellation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSetup), nameof(ShowStart))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSetup), nameof(ShowStart))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool isFinished;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStart))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool ready;

    public bool ShowSetup => !IsBusy && !IsFinished;
    public bool ShowStart => ShowSetup && Ready;

    [ObservableProperty]
    private string backupPath = string.Empty;

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
    [NotifyPropertyChangedFor(nameof(HasReport))]
    [NotifyCanExecuteChangedFor(nameof(OpenFolderCommand))]
    private string? reportPath;

    public bool HasReport => ReportPath is not null;

    public async Task InitializeAsync(string path)
    {
        BackupPath = path;
        IsPreparing = true;
        ProcessedCount = 0;
        TotalCount = 0;
        IsBusy = true;
        Status = Strings.HistoryImportReading;
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        try
        {
            _plan = await Task.Run(() => HistoryImporter.PrepareAsync(path, cancellation.Token));
            TotalCount = _plan.Records.Count;
            Ready = TotalCount > 0;
            Status = Ready ? string.Format(Strings.HistoryImportCount, _plan.Records.Count) : Strings.HistoryImportEmpty;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Status = Strings.HistoryImportCanceled;
            IsFinished = true;
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.HistoryImportFailed, ex.Message);
            IsFinished = true;
        }
        finally
        {
            _cancellation = null;
            IsBusy = false;
            IsPreparing = false;
        }
    }

    private bool CanStart() => ShowStart;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (_plan is null)
            return;
        IsBusy = true;
        Ready = false;
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        ProcessedCount = 0;
        TotalCount = _plan.Records.Count;
        Status = string.Format(Strings.HistoryImportProgress, ProcessedCount, TotalCount);
        var progress = new Progress<int>(count =>
        {
            if (IsBusy)
            {
                ProcessedCount = count;
                Status = string.Format(Strings.HistoryImportProgress, ProcessedCount, TotalCount);
            }
        });
        try
        {
            var result = await Task.Run(() => importer.ImportAsync(_plan, progress, cancellation.Token));
            IsBusy = false;
            ProcessedCount = result.ImportedCount + result.ExistingCount + result.RepairedCount + result.FailureCount;
            Status = HistoryImportReportWriter.FormatResult(result);
            if (result.Canceled)
                Status += "\n" + Strings.HistoryImportCanceled;
            ReportPath = result.ReportPath;
            if (result.ReportError is not null)
                Status += "\n" + string.Format(Strings.HistoryExportReportFailed, result.ReportError);
        }
        catch (Exception ex)
        {
            Status = string.Format(Strings.HistoryImportFailed, ex.Message);
        }
        finally
        {
            _plan.Dispose();
            _plan = null;
            _cancellation = null;
            IsFinished = true;
            IsBusy = false;
        }
    }

    private bool CanOpenFolder() => HasReport;

    [RelayCommand(CanExecute = nameof(CanOpenFolder))]
    private void OpenFolder()
    {
        try
        {
            Sys.OpenFolderInFileManager(Path.GetDirectoryName(ReportPath!)!);
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }

    public void Cancel() => _cancellation?.Cancel();

    public void Dispose()
    {
        Cancel();
        _plan?.Dispose();
    }
}
