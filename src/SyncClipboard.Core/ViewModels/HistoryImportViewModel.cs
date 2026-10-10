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
    [NotifyPropertyChangedFor(nameof(HasReport))]
    [NotifyCanExecuteChangedFor(nameof(OpenFolderCommand))]
    private string? reportPath;

    public bool HasReport => ReportPath is not null;

    public async Task InitializeAsync(string path)
    {
        BackupPath = path;
        IsBusy = true;
        Status = Strings.HistoryImportReading;
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        try
        {
            _plan = await Task.Run(() => HistoryImporter.PrepareAsync(path, cancellation.Token));
            Ready = _plan.Records.Count > 0;
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
        Status = string.Format(Strings.HistoryImportProgress, 0, _plan.Records.Count);
        var progress = new Progress<int>(count =>
        {
            if (IsBusy)
                Status = string.Format(Strings.HistoryImportProgress, count, _plan.Records.Count);
        });
        try
        {
            var result = await Task.Run(() => importer.ImportAsync(_plan, progress, cancellation.Token));
            IsBusy = false;
            Status = HistoryImportReportWriter.FormatResult(result);
            if (result.Canceled)
                Status += "\n" + Strings.HistoryImportCanceled;
            ReportPath = result.ReportPath;
            if (result.ReportError is not null)
            {
                Status += "\n" + string.Format(Strings.HistoryExportReportFailed, result.ReportError);
                Status += "\n" + string.Join('\n', result.Failures.Select(item => $"{item.Index}. {item.ProfileId}: {item.Reason}"));
            }
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
