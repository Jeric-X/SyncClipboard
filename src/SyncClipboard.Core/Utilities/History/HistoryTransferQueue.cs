using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Exceptions;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace SyncClipboard.Core.Utilities.History;

public class HistoryTransferQueue(
    ILogger logger,
    HistorySyncSession syncSession,
    HistoryManager historyManager,
    IProfileEnv profileEnv,
    ConfigManager configManager) : IDisposable, IAsyncDisposable
{
    private readonly ILogger _logger = logger;
    private readonly HistorySyncSession _syncSession = syncSession;
    private readonly HistoryManager _historyManager = historyManager;
    private readonly IProfileEnv _profileEnv = profileEnv;
    private readonly ConfigManager _configManager = configManager;

    // 队列和任务存储
    private readonly Queue<TransferTask> _pendingTasks = new();
    private readonly ConcurrentDictionary<string, TransferTask> _activeTasks = new();
    public readonly SemaphoreSlim ActiveTaskAddMutex = new(1, 1);
    private readonly Lock _queueLock = new();
    private readonly HashSet<TransferTask> _executingTasks = [];

    // 并发控制
    private readonly SemaphoreSlim _workerSemaphore = new(4, 4); // 并行度=4
    private readonly SemaphoreSlim _startSemaphore = new(1, 1); // 确保启动方法的并发安全
    private readonly AutoResetEvent _queueSignal = new(false); // 条件变量：通知队列有新任务
    private readonly CancellationTokenSource _globalCts = new();
    private bool _disposed;
    private Task? _releaseTask;

    // 失败管理
    private int _consecutiveFailures = 0;
    private bool _isQueueStopped = false;
    private Task? _processingTask;

    // 事件：任务状态变化时触发
    public event EventHandler<TransferTask>? TaskStatusChanged;

    public async Task<TransferTask> EnqueueDownload(Profile profile, bool forceResume = false, CancellationToken ct = default)
    {
        return await EnqueueTask(TransferType.Download, profile, _syncSession.Capture(), forceResume, ct);
    }

    public async Task<TransferTask> EnqueueUpload(Profile profile, bool forceResume = false, CancellationToken ct = default)
    {
        return await EnqueueTask(TransferType.Upload, profile, _syncSession.Capture(), forceResume, ct);
    }

    internal Task<TransferTask> EnqueueDownload(Profile profile, HistorySyncSession.Context session,
        bool forceResume = false, CancellationToken ct = default) =>
        EnqueueTask(TransferType.Download, profile, session, forceResume, ct);

    internal Task<TransferTask> EnqueueUpload(Profile profile, HistorySyncSession.Context session,
        bool forceResume = false, CancellationToken ct = default) =>
        EnqueueTask(TransferType.Upload, profile, session, forceResume, ct);

    public Task Download(Profile profile, IProgress<HttpDownloadProgress>? progress = null, CancellationToken ct = default)
    {
        return ExecuteImmediateTask(TransferType.Download, profile, progress, ct);
    }

    public Task Upload(Profile profile, IProgress<HttpDownloadProgress>? progress = null, CancellationToken ct = default)
    {
        return ExecuteImmediateTask(TransferType.Upload, profile, progress, ct);
    }

    private bool GetRunningTask(string taskId, HistorySyncSession.Context session,
        IProgress<HttpDownloadProgress>? progress, [NotNullWhen(true)] out TransferTask? task)
    {
        if (_activeTasks.TryGetValue(taskId, out var existingTask))
        {
            lock (existingTask)
            {
                if (!existingTask.IsRemoved && existingTask.Status == TransferTaskStatus.Running &&
                    existingTask.Session?.AccountToken == session.AccountToken)
                {
                    existingTask.IsImmediateTask = true;
                    existingTask.AutomaticCancellation.Unregister();
                    existingTask.CallerCancellation.Unregister();
                    if (progress is not null)
                        existingTask.ExternalProgressReporter = progress;
                    task = existingTask;
                    return true;
                }
            }
            CancelTask(existingTask, backgroundOnly: false);
        }
        task = null;
        return false;
    }

    private async Task ExecuteImmediateTask(TransferType type, Profile profile, IProgress<HttpDownloadProgress>? progress, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var session = _syncSession.Capture();
        using var request = session.Link(ct, automatic: false);
        ct = request?.Token ?? session.AccountToken;
        var profileId = await profile.GetProfileId(ct);
        var taskId = TransferTask.GetTaskId(type, profileId);
        TransferTask task;
        bool shared;
        await ActiveTaskAddMutex.WaitAsync(ct);
        using (new ScopeGuard(() => ActiveTaskAddMutex.Release()))
        {
            session.ThrowIfCancellationRequested(automatic: false);
            ct.ThrowIfCancellationRequested();
            shared = GetRunningTask(taskId, session, progress, out var existingTask);
            task = shared ? existingTask! : new TransferTask
            {
                ProfileId = profileId,
                Type = type,
                Profile = profile,
                Session = session,
                CreatedTime = DateTime.Now,
                CancellationSource = new CancellationTokenSource(),
                Status = TransferTaskStatus.Pending,
                IsImmediateTask = true,
                ExternalProgressReporter = progress
            };
            if (!shared)
                RegisterTask(task, CancellationToken.None);
        }

        using var callerCancellation = shared ? default : ct.Register(() => CancelTask(task, backgroundOnly: false));
        var status = shared
            ? await task.CompletionSource.Task.WaitAsync(ct)
            : await ExecuteTaskAsync(task).WaitAsync(ct);
        if (status == TransferTaskStatus.Cancelled)
            throw new OperationCanceledException(ct);
        if (status != TransferTaskStatus.Completed)
            throw new InvalidOperationException(task.ErrorMessage ?? $"Transfer {status}");
        if (shared)
            task.Profile.CopyTo(profile);
    }

    private async Task<TransferTask> EnqueueTask(TransferType type, Profile profile, HistorySyncSession.Context session,
        bool forceResume = false, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var callerToken = ct;
        using var request = session.Link(ct);
        ct = request?.Token ?? session.AutomaticToken;
        var profileId = await profile.GetProfileId(ct);
        var taskId = TransferTask.GetTaskId(type, profileId);
        await ActiveTaskAddMutex.WaitAsync(ct);
        using var guard = new ScopeGuard(() => ActiveTaskAddMutex.Release());
        session.ThrowIfCancellationRequested();
        ct.ThrowIfCancellationRequested();
        if (forceResume)
            ResumeQueue();
        if (_activeTasks.TryGetValue(taskId, out var existingTask))
        {
            lock (existingTask)
            {
                if (!existingTask.IsRemoved && existingTask.Session?.AccountToken == session.AccountToken &&
                    (existingTask.IsImmediateTask || existingTask.Session.AutomaticToken == session.AutomaticToken))
                    return existingTask;
            }
        }
        var task = new TransferTask
        {
            ProfileId = profileId,
            Type = type,
            Profile = profile,
            Session = session,
            CreatedTime = DateTime.Now,
            CancellationSource = new CancellationTokenSource(),
            Status = TransferTaskStatus.Pending
        };
        RegisterTask(task, callerToken);
        lock (_queueLock)
        {
            lock (task)
            {
                if (!task.IsRemoved)
                    _pendingTasks.Enqueue(task);
            }
        }
        if (forceResume)
            ResumeQueue();
        await EnsureProcessingTaskStartedAsync(ct);
        lock (_queueLock)
        {
            if (!_disposed)
                _queueSignal.Set();
        }
        return task;
    }

    private void RegisterTask(TransferTask task, CancellationToken callerToken = default)
    {
        lock (_queueLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _activeTasks[task.TaskId] = task;
        }
        // Registrations can invoke cancellation synchronously; publish them without holding task locks.
        var account = task.Session!.AccountToken.Register(() => CancelTask(task, backgroundOnly: false));
        var automatic = task.IsImmediateTask ? default :
            task.Session.AutomaticToken.Register(() => CancelTask(task, backgroundOnly: true));
        var caller = task.IsImmediateTask ? default :
            callerToken.Register(() => CancelTask(task, backgroundOnly: true));
        lock (task)
        {
            task.AccountCancellation = account;
            task.AutomaticCancellation = automatic;
            task.CallerCancellation = caller;
            if (task.IsRemoved)
            {
                account.Unregister();
                automatic.Unregister();
                caller.Unregister();
            }
        }
        NotifyStatusChanged(task);
    }

    public TransferTask? GetTaskByProfileId(string profileId)
    {
        var downloadTaskId = TransferTask.GetTaskId(TransferType.Download, profileId);
        if (_activeTasks.TryGetValue(downloadTaskId, out var downloadTask))
        {
            return downloadTask;
        }

        var uploadTaskId = TransferTask.GetTaskId(TransferType.Upload, profileId);
        if (_activeTasks.TryGetValue(uploadTaskId, out var uploadTask))
        {
            return uploadTask;
        }

        return null;
    }

    public Task<List<TransferTask>> GetAllActiveTasks(CancellationToken token)
    {
        return Task.Run(() =>
         {
             return _activeTasks.Values
                 .OrderBy(t => t.CreatedTime)
                 .ToList();
         }, token);
    }

    public async Task WaitAllTasks(CancellationToken token)
    {
        var tasks = await GetAllActiveTasks(token);
        var completionTasks = tasks
            .Where(t => t.Status == TransferTaskStatus.Running ||
                        t.Status == TransferTaskStatus.Pending ||
                        t.Status == TransferTaskStatus.WaitForRetry)
            .Select(t => t.CompletionSource.Task)
            .ToList();

        if (completionTasks.Count != 0)
        {
            await Task.WhenAll(completionTasks).WaitAsync(token);
        }
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

    public void DeleteTask(string profileId)
    {
        var taskId = TransferTask.GetTaskId(TransferType.Upload, profileId);
        CancelTask(taskId);
        taskId = TransferTask.GetTaskId(TransferType.Download, profileId);
        CancelTask(taskId);
    }

    private bool CancelTask(string taskId)
    {
        if (!_activeTasks.TryGetValue(taskId, out var task))
            return false;
        CancelTask(task, backgroundOnly: false);
        return true;
    }

    private void CancelTask(TransferTask task, bool backgroundOnly)
    {
        lock (task)
        {
            if (task.IsRemoved || (backgroundOnly && task.IsImmediateTask))
                return;
            task.IsCancelling = true;
            task.Status = TransferTaskStatus.Cancelled;
            task.CompletionSource.TrySetResult(TransferTaskStatus.Cancelled);
            RemoveTask(task);
        }
        try
        {
            task.CancellationSource.Cancel();
        }
        finally
        {
            lock (task)
            {
                task.IsCancelling = false;
                if (!task.IsExecuting)
                    task.CancellationSource.Dispose();
            }
        }
        NotifyStatusChanged(task);
    }

    public void ClearQueue()
    {
        foreach (var task in _activeTasks.Values.ToArray())
            CancelTask(task, backgroundOnly: false);
        lock (_queueLock)
        {
            var remaining = _pendingTasks.Where(task => !task.IsRemoved).ToArray();
            _pendingTasks.Clear();
            foreach (var task in remaining)
                _pendingTasks.Enqueue(task);
        }
    }

    /// <summary>
    /// 恢复队列处理（失败停止后恢复）
    /// </summary>
    public void ResumeQueue()
    {
        lock (_queueLock)
        {
            if (_disposed || !_isQueueStopped)
                return;
            _isQueueStopped = false;
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            _logger.Write("队列已恢复");
            if (_pendingTasks.Count > 0)
                _queueSignal.Set();
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
                    ObjectDisposedException.ThrowIf(_disposed, this);
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

    public void Dispose()
    {
        Task[] executions;
        lock (_queueLock)
        {
            if (_disposed)
                return;
            _disposed = true;
            executions = _executingTasks.Select(task => task.ExecutionFinished!.Task).ToArray();
        }
        foreach (var task in _activeTasks.Values.ToArray())
            CancelTask(task, backgroundOnly: false);
        _globalCts.Cancel();
        _releaseTask = ReleaseQueueResourcesAsync(executions);
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        if (_releaseTask is not null)
            await _releaseTask.ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private async Task ReleaseQueueResourcesAsync(Task[] executions)
    {
        await _startSemaphore.WaitAsync().ConfigureAwait(false);
        _startSemaphore.Release();
        try
        {
            await Task.WhenAll(executions).ConfigureAwait(false);
            if (_processingTask is not null)
                await _processingTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _globalCts.Dispose();
            _queueSignal.Dispose();
            _workerSemaphore.Dispose();
        }
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

            if (task.IsRemoved || task.Status == TransferTaskStatus.Cancelled)
            {
                continue;
            }

            await _workerSemaphore.WaitAsync(token);
            if (task.Status == TransferTaskStatus.WaitForRetry)
            {
                var now = DateTime.Now;
                var restartTime = task.CompletedTime + TimeSpan.FromSeconds(3);
                if (now < restartTime)
                {
                    TimeSpan time = restartTime - now ?? TimeSpan.Zero;
                    await Task.Delay(time, token);
                }
            }

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
            }, CancellationToken.None);

            workers.Add(worker);
            workers.RemoveAll(w => w.IsCompleted);
        }
    }

    private async Task<TransferTaskStatus> ExecuteTaskAsync(TransferTask task)
    {
        lock (_queueLock)
            lock (task)
            {
                if (task.IsRemoved)
                    return task.Status;
                if (_disposed)
                {
                    task.Status = TransferTaskStatus.Cancelled;
                    task.CompletionSource.TrySetResult(task.Status);
                    RemoveTask(task);
                    return task.Status;
                }
                task.ExecutionFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                task.IsExecuting = true;
                _executingTasks.Add(task);
            }
        try
        {
            await ExecuteTaskCoreAsync(task, task.CancellationSource.Token);
            return TransferTaskStatus.Completed;
        }
        catch (Exception ex) when (ex is OperationCanceledException || task.CancellationSource.IsCancellationRequested)
        {
            task.Status = TransferTaskStatus.Cancelled;
            _logger.Write($"任务 {task.TaskId} 已取消");
            NotifyStatusChanged(task);
            task.CompletionSource.TrySetResult(TransferTaskStatus.Cancelled);
            RemoveTask(task);
            return TransferTaskStatus.Cancelled;
        }
        catch (LocalProfileDataUnavailableException ex)
        {
            return await HandleUnavailableDataAsync(task, ex);
        }
        catch (Exception ex)
        {
            return HandleTaskFailure(task, ex);
        }
        finally
        {
            FinishTaskExecution(task);
        }
    }

    private async Task<TransferTaskStatus> HandleUnavailableDataAsync(TransferTask task, Exception error)
    {
        if (task.Type == TransferType.Upload && Profile.ParseProfileId(task.ProfileId, out var type, out var hash))
        {
            try
            {
                task.Session?.ThrowIfCancellationRequested(automatic: !task.IsImmediateTask);
                await _historyManager.HandleLocalFileUnavailableAsync(type, hash, task.CancellationSource.Token);
            }
            catch (OperationCanceledException)
            {
                CancelTask(task, backgroundOnly: false);
                return TransferTaskStatus.Cancelled;
            }
            catch (Exception persistException)
            {
                _logger.Write($"任务 {task.TaskId} 标记本地文件不可用失败: {persistException.Message}");
            }
        }
        lock (task)
        {
            if (task.IsRemoved)
                return task.Status;
            task.ErrorMessage = I18n.Strings.SourceFileMissingUnableToUpload;
            task.FailureCount++;
            task.Status = TransferTaskStatus.Failed;
            task.CompletedTime = DateTime.Now;
            task.CompletionSource.TrySetResult(TransferTaskStatus.Failed);
            RemoveTask(task);
        }
        _logger.Write($"任务 {task.TaskId} 因本地文件缺失失败: {error.Message}");
        NotifyStatusChanged(task);
        return task.Status;
    }

    private TransferTaskStatus HandleTaskFailure(TransferTask task, Exception error)
    {
        int currentFailures;
        lock (task)
        {
            if (task.IsRemoved)
                return task.Status;
            task.ErrorMessage = error.Message;
            task.FailureCount++;
            currentFailures = Interlocked.Increment(ref _consecutiveFailures);
            task.CompletedTime = DateTime.Now;
            if (currentFailures >= 5)
            {
                task.Status = TransferTaskStatus.Failed;
                task.CompletionSource.TrySetResult(TransferTaskStatus.Failed);
                RemoveTask(task);
            }
            else
            {
                task.Status = TransferTaskStatus.WaitForRetry;
                task.StartedTime = null;
            }
        }
        _logger.Write($"任务 {task.TaskId} 失败 (连续失败: {currentFailures}/5): {error.Message}");
        if (currentFailures >= 5)
        {
            MarkAllQueuedTasksAsFailed();
            _logger.Write("队列因连续失败5次已停止");
        }
        NotifyStatusChanged(task);
        return task.Status;
    }

    private void FinishTaskExecution(TransferTask task)
    {
        lock (_queueLock)
            lock (task)
            {
                task.IsExecuting = false;
                if (task.IsRemoved)
                {
                    if (!task.IsCancelling)
                        task.CancellationSource.Dispose();
                }
                else if (task.Status == TransferTaskStatus.WaitForRetry)
                {
                    if (_disposed || _isQueueStopped)
                    {
                        task.Status = _disposed ? TransferTaskStatus.Cancelled : TransferTaskStatus.Failed;
                        task.CompletionSource.TrySetResult(task.Status);
                        RemoveTask(task);
                    }
                    else
                    {
                        _pendingTasks.Enqueue(task);
                        _queueSignal.Set();
                    }
                }
                _executingTasks.Remove(task);
                task.ExecutionFinished?.TrySetResult();
            }
    }

    private async Task ExecuteTaskCoreAsync(TransferTask task, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        task.Session?.ThrowIfCancellationRequested(automatic: !task.IsImmediateTask);
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

        lock (task)
        {
            ct.ThrowIfCancellationRequested();
            if (task.IsRemoved)
                throw new OperationCanceledException(ct);
            task.Status = TransferTaskStatus.Completed;
            task.CompletedTime = DateTime.Now;
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            task.CompletionSource.TrySetResult(TransferTaskStatus.Completed);
            RemoveTask(task);
        }
        NotifyStatusChanged(task);
    }

    private async Task ExecuteDownloadAsync(TransferTask task, CancellationToken ct)
    {
        if (task.Session?.Server is not IOfficialSyncServer server)
        {
            throw new InvalidOperationException("当前服务器不支持历史记录同步");
        }

        var profile = task.Profile;
        var persistentDir = _profileEnv.GetPersistentDir();

        if (await profile.TryLocalize(persistentDir, true, ct))
        {
            return;
        }

        var localDataPath = profile.GetTransferDataSavePath(persistentDir)
            ?? throw new InvalidOperationException("Profile does not support transfer data.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(localDataPath))!);
        var transferDataHash = await server.DownloadHistoryDataAsync(
            task.ProfileId, localDataPath, task.ProgressReporter, ct);
        ct.ThrowIfCancellationRequested();
        task.Session.ThrowIfCancellationRequested(automatic: !task.IsImmediateTask);
        if (Utility.IsValidSHA256(transferDataHash))
        {
            await profile.SetTransferData(new FileHashInfo(localDataPath, transferDataHash), false, ct);
        }
        else
        {
            await profile.SetTransferData(localDataPath, verify: true, ct);
        }
        if (profile is GroupProfile)
        {
            await profile.Localize(persistentDir, ct);
        }
        if (_configManager.GetConfig<HistoryConfig>().EnableHistory)
        {
            await _historyManager.AddLocalProfile(profile, updateLastAccessed: false, token: ct);
        }
    }

    private async Task ExecuteUploadAsync(TransferTask task, CancellationToken ct)
    {
        if (task.Session?.Server is not IOfficialSyncServer server)
        {
            throw new InvalidOperationException("当前服务器不支持历史记录同步");
        }

        var profile = task.Profile;
        var record = await _historyManager.GetOrCreateHistoryRecord(profile, ct);

        // 先检查服务器上是否已存在该记录
        var existingRecord = await server.GetHistoryByProfileIdAsync(task.ProfileId, ct);
        ct.ThrowIfCancellationRequested();
        task.Session.ThrowIfCancellationRequested(automatic: !task.IsImmediateTask);
        if (existingRecord is { IsDeleted: false })
        {
            // 服务器已有正常记录，直接标记为成功
            record.SyncStatus = HistorySyncStatus.Synced;
            await _historyManager.PersistServerSyncedAsync(record, ct);
            return;
        }

        // 服务器不存在或记录已删除，执行上传以恢复内容和状态
        var transferData = await profile.PrepareTransferData(_profileEnv.GetPersistentDir(), ct);
        var persistentInfo = await profile.Persist(_profileEnv.GetPersistentDir(), ct);
        record.FilePath = persistentInfo.FilePaths;
        record.TransferDataFile = persistentInfo.TransferDataFile;
        record.TransferDataHash = persistentInfo.TransferDataHash;
        record.IsLocalFileReady = true;
        await _historyManager.UpdateHistoryLocalInfo(record, ct);
        var recordDto = record.ToHistoryRecordDto();

        try
        {
            var serverRecord = await server.UploadHistoryAsync(recordDto, transferData, task.ProgressReporter, ct);
            ct.ThrowIfCancellationRequested();
            task.Session.ThrowIfCancellationRequested(automatic: !task.IsImmediateTask);
            record.ApplyChangesFromRemote(serverRecord);
            record.ApplyBasicFromRemote(serverRecord);
            record.SyncStatus = HistorySyncStatus.Synced;
            await _historyManager.PersistServerSyncedAsync(record, ct);
        }
        catch (RemoteHistoryConflictException ex)
        {
            ct.ThrowIfCancellationRequested();
            task.Session.ThrowIfCancellationRequested(automatic: !task.IsImmediateTask);
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
        lock (task)
        {
            if (task.IsRemoved)
                return;
            task.IsRemoved = true;
            _activeTasks.TryRemove(new KeyValuePair<string, TransferTask>(task.TaskId, task));
            task.AccountCancellation.Unregister();
            task.AutomaticCancellation.Unregister();
            task.CallerCancellation.Unregister();
            if (!task.IsExecuting && !task.IsCancelling)
                task.CancellationSource.Dispose();
        }
    }

    private void MarkAllQueuedTasksAsFailed()
    {
        TransferTask[] queued;
        lock (_queueLock)
        {
            _isQueueStopped = true;
            queued = _pendingTasks.ToArray();
            _pendingTasks.Clear();
        }
        foreach (var task in queued)
        {
            lock (task)
            {
                if (task.IsRemoved)
                    continue;
                task.Status = TransferTaskStatus.Failed;
                task.ErrorMessage = "队列已停止";
                task.CompletionSource.TrySetResult(TransferTaskStatus.Failed);
                RemoveTask(task);
            }
            NotifyStatusChanged(task);
        }
    }
}
