using System.Collections.Concurrent;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.RemoteServer;
using SyncClipboard.Core.Exceptions;
using System.Diagnostics.CodeAnalysis;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.Utilities.History;

public class HistoryTransferQueue : IDisposable
{
    private readonly ILogger _logger;
    private readonly RemoteClipboardServerFactory _remoteServerFactory;
    private readonly HistoryManager _historyManager;
    private readonly IProfileEnv _profileEnv;
    private readonly ConfigManager _configManager;

    // 队列和任务存储
    private readonly Queue<TransferTask> _pendingTasks = new();
    private readonly ConcurrentDictionary<string, TransferTask> _activeTasks = new();
    private readonly object _queueLock = new();

    // 并发控制
    private readonly SemaphoreSlim _workerSemaphore = new(5, 5); // 并行度=5
    private readonly SemaphoreSlim _startSemaphore = new(1, 1); // 确保启动方法的并发安全
    private readonly AutoResetEvent _queueSignal = new(false); // 条件变量：通知队列有新任务
    private CancellationTokenSource _globalCts = new();

    // 失败管理
    private int _consecutiveFailures = 0;
    private bool _isQueueStopped = false;
    private Task? _processingTask;

    // 事件：任务状态变化时触发
    public event EventHandler<TransferTask>? TaskStatusChanged;

    public HistoryTransferQueue(
        ILogger logger,
        RemoteClipboardServerFactory remoteServerFactory,
        HistoryManager historyManager,
        IProfileEnv profileEnv,
        ConfigManager configManager)
    {
        _logger = logger;
        _remoteServerFactory = remoteServerFactory;
        _historyManager = historyManager;
        _profileEnv = profileEnv;
        _configManager = configManager;
        _remoteServerFactory.CurrentServerChanged += (_, _) => ClearQueue();
        _configManager.ListenConfig<HistoryConfig>(OnSyncHistoryChanged);
    }

    public async Task<TransferTask> EnqueueDownload(Profile profile, CancellationToken ct = default)
    {
        return await EnqueueTask(TransferType.Download, profile, ct);
    }

    public async Task<TransferTask> EnqueueUpload(Profile profile, CancellationToken ct = default)
    {
        return await EnqueueTask(TransferType.Upload, profile, ct);
    }

    public Task Download(Profile profile, IProgress<HttpDownloadProgress>? progress = null, CancellationToken ct = default)
    {
        return ExecuteImmediateTask(TransferType.Download, profile, progress, ct);
    }

    public Task Upload(Profile profile, IProgress<HttpDownloadProgress>? progress = null, CancellationToken ct = default)
    {
        return ExecuteImmediateTask(TransferType.Upload, profile, progress, ct);
    }

    private bool GetRunningTask(string taskId, [NotNullWhen(true)] out TransferTask? task)
    {
        if (_activeTasks.TryGetValue(taskId, out var existingTask))
        {
            if (existingTask.Status == TransferTaskStatus.Running)
            {
                task = existingTask;
                return true;
            }
            else
            {
                try
                {
                    existingTask.CancellationSource?.Cancel();
                }
                catch { }
            }
        }

        task = null;
        return false;
    }

    private async Task ExecuteImmediateTask(TransferType type, Profile profile, IProgress<HttpDownloadProgress>? progress, CancellationToken ct)
    {
        var profileId = await profile.GetProfileId(ct);
        var taskId = TransferTask.GetTaskId(type, profileId);

        if (GetRunningTask(taskId, out var runningTask))
        {
            runningTask.IsImmediateTask = true;
            if (progress != null)
            {
                runningTask.ExternalProgressReporter = progress;
            }
            await runningTask.CompletionSource.Task.WaitAsync(ct);
            return;
        }

        var task = new TransferTask
        {
            ProfileId = profileId,
            Type = type,
            Profile = profile,
            CreatedTime = DateTime.Now,
            CancellationSource = new CancellationTokenSource(),
            Status = TransferTaskStatus.Pending,
            IsImmediateTask = true,
            ExternalProgressReporter = progress
        };

        _activeTasks.AddOrUpdate(task.TaskId, task, (_, _) => task);

        var status = await ExecuteTaskAsync(task).WaitAsync(ct);
        if (status != TransferTaskStatus.Completed)
        {
            throw new Exception(task.ErrorMessage);
        }
    }

    private async Task<TransferTask> EnqueueTask(TransferType type, Profile profile, CancellationToken ct)
    {
        var profileId = await profile.GetProfileId(ct);
        var taskId = TransferTask.GetTaskId(type, profileId);

        if (_activeTasks.TryGetValue(taskId, out var existingTask))
        {
            _logger.Write($"{type} 任务 {taskId} 已存在，返回现有任务");
            return existingTask;
        }

        var task = new TransferTask
        {
            ProfileId = profileId,
            Type = type,
            Profile = profile,
            CreatedTime = DateTime.Now,
            CancellationSource = new CancellationTokenSource(),
            Status = TransferTaskStatus.Pending
        };

        _activeTasks.TryAdd(taskId, task);

        lock (_queueLock)
        {
            _pendingTasks.Enqueue(task);
        }

        _logger.Write($"{type} 任务 {taskId} 已加入队列");

        await EnsureProcessingTaskStartedAsync(ct);
        _queueSignal.Set();

        return task;
    }

    public TransferTask? GetTask(string taskId)
    {
        _activeTasks.TryGetValue(taskId, out var task);
        return task;
    }

    public bool CancelDownload(string profileId)
    {
        var taskId = TransferTask.GetTaskId(TransferType.Download, profileId);
        return CancelTask(taskId);
    }

    public bool CancelUpload(string profileId)
    {
        var taskId = TransferTask.GetTaskId(TransferType.Upload, profileId);
        return CancelTask(taskId);
    }

    private bool CancelTask(string taskId)
    {
        if (_activeTasks.TryGetValue(taskId, out var task))
        {
            task.Status = TransferTaskStatus.Cancelled;
            NotifyStatusChanged(task);

            try
            {
                task.CancellationSource?.Cancel();
            }
            catch (Exception ex)
            {
                _logger.Write($"取消任务 {taskId} 时出错: {ex.Message}");
            }

            if (task.StartedTime == null)
            {
                RemoveTask(task);
            }

            return true;
        }

        return false;
    }

    public void ClearQueue()
    {
        _logger.Write("清空传输队列");

        // 取消所有任务
        foreach (var task in _activeTasks.Values)
        {
            try
            {
                task.CancellationSource?.Cancel();
                task.Status = TransferTaskStatus.Cancelled;
                NotifyStatusChanged(task);
            }
            catch { }
        }

        lock (_queueLock)
        {
            _pendingTasks.Clear();
        }

        // 移除所有任务
        var allTaskIds = _activeTasks.Keys.ToList();
        foreach (var taskId in allTaskIds)
        {
            if (_activeTasks.TryRemove(taskId, out var task))
            {
                RemoveTask(task);
            }
        }
    }

    /// <summary>
    /// 恢复队列处理（失败停止后恢复）
    /// </summary>
    public void ResumeQueue()
    {
        if (_isQueueStopped)
        {
            _isQueueStopped = false;
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            _logger.Write("队列已恢复");

            // 通知处理任务恢复处理
            int pendingCount;
            lock (_queueLock)
            {
                pendingCount = _pendingTasks.Count;
            }

            // 如果有待处理任务，发送信号
            if (pendingCount > 0)
            {
                _queueSignal.Set();
            }
        }
    }

    private async Task EnsureProcessingTaskStartedAsync(CancellationToken cancellationToken = default)
    {
        if (_processingTask == null || _processingTask.IsCompleted)
        {
            await _startSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (_processingTask == null || _processingTask.IsCompleted)
                {
                    _globalCts = new CancellationTokenSource();
                    _processingTask = Task.Run(() => ProcessQueueAsync(_globalCts.Token), CancellationToken.None);
                    _logger.Write("传输队列已启动");
                }
            }
            finally
            {
                _startSemaphore.Release();
            }
        }
    }

    public void Stop()
    {
        _globalCts?.Cancel();
        _processingTask?.Wait(TimeSpan.FromSeconds(5));
        _logger.Write("传输队列已停止");
    }

    ~HistoryTransferQueue()
    {
        Dispose();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Stop();
        _globalCts?.Dispose();
        _workerSemaphore?.Dispose();
        _startSemaphore?.Dispose();
        _queueSignal?.Dispose();

        foreach (var task in _activeTasks.Values)
        {
            task.CancellationSource?.Dispose();
        }
        _activeTasks.Clear();
    }

    private async Task ProcessQueueAsync(CancellationToken token)
    {
        var workers = new List<Task>();

        try
        {
            while (!token.IsCancellationRequested)
            {
                WaitHandle.WaitAny([_queueSignal, token.WaitHandle]);
                if (token.IsCancellationRequested)
                    break;

                if (_isQueueStopped)
                    continue;

                await ProcessPendingTasksAsync(workers, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (workers.Count > 0)
            {
                await Task.WhenAll(workers);
            }
        }
    }

    private async Task ProcessPendingTasksAsync(List<Task> workers, CancellationToken token)
    {
        while (true)
        {
            TransferTask? task = null;
            lock (_queueLock)
            {
                if (_pendingTasks.Count > 0)
                {
                    task = _pendingTasks.Dequeue();
                }
            }

            if (task == null)
            {
                break;
            }

            if (task.Status == TransferTaskStatus.Cancelled)
            {
                continue;
            }

            await _workerSemaphore.WaitAsync(token);

            var worker = Task.Run(async () =>
            {
                try
                {
                    await ExecuteTaskAsync(task);
                }
                finally
                {
                    _workerSemaphore.Release();
                }
            }, token);

            workers.Add(worker);
            workers.RemoveAll(w => w.IsCompleted);
        }
    }

    private async Task<TransferTaskStatus> ExecuteTaskAsync(TransferTask task)
    {
        try
        {
            await ExecuteTaskCoreAsync(task, task.CancellationSource.Token);
            return TransferTaskStatus.Completed;
        }
        catch (OperationCanceledException)
        {
            task.Status = TransferTaskStatus.Cancelled;
            _logger.Write($"任务 {task.TaskId} 已取消");
            NotifyStatusChanged(task);
            task.CompletionSource.TrySetResult(TransferTaskStatus.Cancelled);
            RemoveTask(task);
            return TransferTaskStatus.Cancelled;
        }
        catch (Exception ex)
        {
            task.Status = TransferTaskStatus.Failed;
            task.ErrorMessage = ex.Message;
            task.FailureCount++;
            NotifyStatusChanged(task);

            var currentFailures = Interlocked.Increment(ref _consecutiveFailures);
            _logger.Write($"任务 {task.TaskId} 失败 (连续失败: {currentFailures}/5): {ex.Message}");

            if (currentFailures >= 5)
            {
                _isQueueStopped = true;
                MarkAllQueuedTasksAsFailed();
                _logger.Write("队列因连续失败5次已停止");
                task.CompletionSource.TrySetResult(TransferTaskStatus.Failed);
                RemoveTask(task);
                NotifyStatusChanged(task);
                return TransferTaskStatus.Failed;
            }
            else
            {
                lock (_queueLock)
                {
                    _pendingTasks.Enqueue(task);
                    task.Status = TransferTaskStatus.WaitForRetry;
                    task.StartedTime = null;
                }
                NotifyStatusChanged(task);

                _queueSignal.Set();
                return TransferTaskStatus.WaitForRetry;
            }
        }
    }

    private async Task ExecuteTaskCoreAsync(TransferTask task, CancellationToken ct)
    {
        task.Status = TransferTaskStatus.Running;
        task.StartedTime = DateTime.Now;
        NotifyStatusChanged(task);

        if (task.Type == TransferType.Download)
        {
            await ExecuteDownloadAsync(task, ct);
        }
        else
        {
            await ExecuteUploadAsync(task, ct);
        }

        task.Status = TransferTaskStatus.Completed;
        task.CompletedTime = DateTime.Now;
        Interlocked.Exchange(ref _consecutiveFailures, 0);
        NotifyStatusChanged(task);
        task.CompletionSource.TrySetResult(TransferTaskStatus.Completed);
        RemoveTask(task);
    }

    private async Task ExecuteDownloadAsync(TransferTask task, CancellationToken ct)
    {
        if (_remoteServerFactory.Current is not IHistorySyncServer server)
        {
            throw new InvalidOperationException("当前服务器不支持历史记录同步");
        }

        var profile = task.Profile;
        var persistentDir = _profileEnv.GetPersistentDir();

        if (profile.NeedsTransferData(persistentDir, out var localDataPath) is false)
        {
            return;
        }

        await server.DownloadHistoryDataAsync(task.ProfileId, localDataPath, task.ProgressReporter, ct);
        await profile.SetTranseferData(localDataPath, true, ct);
        await _historyManager.AddLocalProfile(profile, ct);
    }

    private async Task ExecuteUploadAsync(TransferTask task, CancellationToken ct)
    {
        if (_remoteServerFactory.Current is not IHistorySyncServer server)
        {
            throw new InvalidOperationException("当前服务器不支持历史记录同步");
        }

        var profile = task.Profile;
        string? transferFilePath = await profile.PrepareTransferData(_profileEnv.GetPersistentDir(), ct);

        var record = await _historyManager.GetOrCreateHistoryRecord(profile, ct);
        var recordDto = record.ToHistoryRecordDto();

        try
        {
            await server.UploadHistoryAsync(recordDto, transferFilePath, task.ProgressReporter, ct);
            record.SyncStatus = HistorySyncStatus.Synced;
        }
        catch (RemoteHistoryConflictException ex)
        {
            if (ex.ServerRecord == null)
            {
                throw;
            }
            record.ApplyFromServerUpdateDto(ex.ServerRecord);
            await _historyManager.PersistServerSyncedAsync(record, ct);
        }
    }

    private void NotifyStatusChanged(TransferTask task)
    {
        TaskStatusChanged?.Invoke(this, task);
    }

    private void RemoveTask(TransferTask task)
    {
        if (_activeTasks.TryRemove(task.TaskId, out _))
        {
            task.CancellationSource?.Dispose();
        }
    }

    private void MarkAllQueuedTasksAsFailed()
    {
        lock (_queueLock)
        {
            while (_pendingTasks.TryDequeue(out var task))
            {
                task.Status = TransferTaskStatus.Failed;
                task.ErrorMessage = "队列已停止";
                NotifyStatusChanged(task);
                task.CompletionSource.TrySetResult(TransferTaskStatus.Failed);
                RemoveTask(task);
            }
        }
    }

    private void OnSyncHistoryChanged(HistoryConfig config)
    {
        if (!config.EnableSyncHistory)
        {
            CancelNonImmediateTasks();
        }
    }

    private void CancelNonImmediateTasks()
    {
        foreach (var task in _activeTasks.Values.Where(t => !t.IsImmediateTask))
        {
            try
            {
                task.CancellationSource?.Cancel();
                task.Status = TransferTaskStatus.Cancelled;
                NotifyStatusChanged(task);
            }
            catch { }
        }

        lock (_queueLock)
        {
            var tasksToRemove = _pendingTasks.Where(t => !t.IsImmediateTask).ToList();
            _pendingTasks.Clear();
            foreach (var task in tasksToRemove)
            {
                task.Status = TransferTaskStatus.Cancelled;
                task.ErrorMessage = "同步历史记录已关闭";
                NotifyStatusChanged(task);
                task.CompletionSource.TrySetResult(TransferTaskStatus.Cancelled);
                RemoveTask(task);
            }
        }
    }
}
