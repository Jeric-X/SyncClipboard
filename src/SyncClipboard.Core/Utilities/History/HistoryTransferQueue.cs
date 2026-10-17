using Microsoft.Extensions.DependencyInjection;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Exceptions;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer;
using System.Collections.Concurrent;
using SyncClipboard.Server.Core.Models;

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
    public readonly SemaphoreSlim ActiveTaskAddMutex = new(1, 1);
    private readonly Lock _queueLock = new();

    // 并发控制
    private readonly SemaphoreSlim _workerSemaphore = new(4, 4); // 并行度=4
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
        ConfigManager configManager,
        [FromKeyedServices(Env.RuntimeConfigName)] ConfigBase runtimeConfig)
    {
        _logger = logger;
        _remoteServerFactory = remoteServerFactory;
        _historyManager = historyManager;
        _profileEnv = profileEnv;
        _configManager = configManager;
        _remoteServerFactory.CurrentServerChanged += (_, _) => ClearQueue();
        runtimeConfig.ListenConfig<RuntimeHistoryConfig>(OnSyncHistoryChanged);
    }

    public async Task<TransferTask> EnqueueDownload(Profile profile, bool forceResume = false, CancellationToken ct = default)
    {
        return await EnqueueTask(TransferType.Download, profile, forceResume, ct: ct);
    }

    public async Task<TransferTask> EnqueueUpload(Profile profile, bool forceResume = false, CancellationToken ct = default)
    {
        return await EnqueueTask(TransferType.Upload, profile, forceResume, ct);
    }

    public Task Download(Profile profile, IProgress<HttpDownloadProgress>? progress = null, CancellationToken ct = default)
    {
        return ExecuteImmediateTask(TransferType.Download, profile, progress, ct);
    }

    public Task Upload(Profile profile, IProgress<HttpDownloadProgress>? progress = null, CancellationToken ct = default)
    {
        return ExecuteImmediateTask(TransferType.Upload, profile, progress, ct);
    }

    private async Task ExecuteImmediateTask(TransferType type, Profile profile, IProgress<HttpDownloadProgress>? progress, CancellationToken ct)
    {
        var task = await GetOrAddTaskAsync(type, profile, immediate: true, progress, ct);
        // A pending background task can start immediately; execution is claimed once per task.
        _ = ExecuteTaskAsync(task);
        var status = await task.CompletionSource.Task.WaitAsync(ct);
        if (status != TransferTaskStatus.Completed)
            throw new InvalidOperationException(task.ErrorMessage ?? $"Transfer {status}");
        task.Profile.CopyTo(profile);
    }

    private async Task<TransferTask> EnqueueTask(TransferType type, Profile profile, bool forceResume = false,
        CancellationToken ct = default)
    {
        var task = await GetOrAddTaskAsync(type, profile, immediate: false, null, ct);
        if (forceResume)
            ResumeQueue();
        return task;
    }

    private async Task<TransferTask> GetOrAddTaskAsync(
        TransferType type, Profile profile, bool immediate, IProgress<HttpDownloadProgress>? progress,
        CancellationToken ct)
    {
        var profileId = await profile.GetProfileId(ct);
        TransferTask task;
        bool created;
        await ActiveTaskAddMutex.WaitAsync(ct);
        try
        {
            // Registration and clearing use the same short lock; no callbacks or awaits inside it.
            lock (_queueLock)
            {
                ct.ThrowIfCancellationRequested();
                (task, created) = GetOrCreateTask(type, profileId, profile, immediate, progress);
                if (created && !immediate)
                    _pendingTasks.Enqueue(task);
            }
        }
        finally
        {
            ActiveTaskAddMutex.Release();
        }
        if (created)
        {
            NotifyStatusChanged(task);
            if (!immediate)
            {
                await EnsureProcessingTaskStartedAsync(CancellationToken.None);
                _queueSignal.Set();
            }
        }
        return task;
    }

    // Called with _queueLock held. When both locks are needed, always take _queueLock before task.
    private (TransferTask Task, bool Created) GetOrCreateTask(
        TransferType type, string profileId, Profile profile, bool immediate, IProgress<HttpDownloadProgress>? progress)
    {
        var taskId = TransferTask.GetTaskId(type, profileId);
        if (_activeTasks.TryGetValue(taskId, out var existingTask))
        {
            lock (existingTask)
            {
                if (IsActiveTask(existingTask) && existingTask.Status is
                    (TransferTaskStatus.Pending or TransferTaskStatus.Running or TransferTaskStatus.WaitForRetry))
                {
                    MergeTaskRequest(existingTask, profile, immediate, progress);
                    return (existingTask, false);
                }
                RemoveTask(existingTask);
            }
        }
        var task = new TransferTask
        {
            ProfileId = profileId,
            Type = type,
            Profile = profile,
            CreatedTime = DateTime.Now,
            CancellationSource = new CancellationTokenSource(),
            Status = TransferTaskStatus.Pending,
            IsImmediateTask = immediate,
            ExternalProgressReporter = progress
        };
        _activeTasks.TryAdd(taskId, task);
        return (task, true);
    }

    // Running requests share the current result; only pending requests replace the content source.
    private static void MergeTaskRequest(
        TransferTask task, Profile profile, bool immediate, IProgress<HttpDownloadProgress>? progress)
    {
        if (task.Status != TransferTaskStatus.Running && task.Type == TransferType.Upload &&
            (immediate || !task.IsImmediateTask))
            task.Profile = profile;
        if (immediate)
        {
            task.IsImmediateTask = true;
            if (progress is not null)
                task.ExternalProgressReporter = progress;
        }
    }

    public TransferTask? GetTaskByProfileId(string profileId, TransferType? type = null)
    {
        var downloadTaskId = TransferTask.GetTaskId(TransferType.Download, profileId);
        if (type != TransferType.Upload && _activeTasks.TryGetValue(downloadTaskId, out var downloadTask))
        {
            return downloadTask;
        }

        var uploadTaskId = TransferTask.GetTaskId(TransferType.Upload, profileId);
        if (type != TransferType.Download && _activeTasks.TryGetValue(uploadTaskId, out var uploadTask))
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
        if (DetachCancelledTask(task, backgroundOnly))
            FinishCancellation(task);
    }

    private bool DetachCancelledTask(TransferTask task, bool backgroundOnly)
    {
        lock (task)
        {
            if (!IsActiveTask(task) || (backgroundOnly && task.IsImmediateTask))
                return false;
            task.Status = TransferTaskStatus.Cancelled;
            _activeTasks.TryRemove(new KeyValuePair<string, TransferTask>(task.TaskId, task));
            return true;
        }
    }

    private void FinishCancellation(TransferTask task)
    {
        try
        {
            // Cancel invokes callbacks synchronously; never hold a queue/task lock here.
            task.CancellationSource.Cancel();
        }
        catch (AggregateException ex)
        {
            _logger.Write($"任务 {task.TaskId} 的取消回调失败: {ex}");
        }
        finally
        {
            task.CancellationSource.Dispose();
            task.CompletionSource.TrySetResult(TransferTaskStatus.Cancelled);
            NotifyStatusChanged(task);
        }
    }

    public void ClearQueue()
    {
        _logger.Write("清空传输队列");
        CancelTasks(backgroundOnly: false);
    }

    private void CancelTasks(bool backgroundOnly)
    {
        List<TransferTask> cancelled = [];
        lock (_queueLock)
        {
            foreach (var task in _activeTasks.Values.ToArray())
            {
                if (DetachCancelledTask(task, backgroundOnly))
                    cancelled.Add(task);
            }
            var pending = _pendingTasks.Where(IsActiveTask).ToArray();
            _pendingTasks.Clear();
            foreach (var task in pending)
                _pendingTasks.Enqueue(task);
        }
        foreach (var task in cancelled)
            FinishCancellation(task);
    }

    /// <summary>
    /// 恢复队列处理（失败停止后恢复）
    /// </summary>
    public void ResumeQueue()
    {
        lock (_queueLock)
        {
            if (!_isQueueStopped)
                return;
            _isQueueStopped = false;
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            if (_pendingTasks.Count > 0)
                _queueSignal.Set();
        }
        _logger.Write("队列已恢复");
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

    ~HistoryTransferQueue()
    {
        Dispose();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        foreach (var task in _activeTasks.Values.ToArray())
            CancelTask(task, backgroundOnly: false);
        _globalCts?.Cancel();
        _globalCts?.Dispose();
        _workerSemaphore?.Dispose();
        _startSemaphore?.Dispose();
        _queueSignal?.Dispose();
        ActiveTaskAddMutex?.Dispose();
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
            }, token);

            workers.Add(worker);
            workers.RemoveAll(w => w.IsCompleted);
        }
    }

    private async Task<TransferTaskStatus> ExecuteTaskAsync(TransferTask task)
    {
        CancellationToken ct;
        lock (task)
        {
            if (!IsActiveTask(task) ||
                task.Status is not (TransferTaskStatus.Pending or TransferTaskStatus.WaitForRetry))
                return task.Status;
            ct = task.CancellationSource.Token;
            task.Status = TransferTaskStatus.Running;
            task.StartedTime = DateTime.Now;
        }
        try
        {
            await ExecuteTaskCoreAsync(task, ct);
            return task.Status;
        }
        catch (OperationCanceledException)
        {
            CancelTask(task, backgroundOnly: false);
            return task.Status;
        }
        catch (LocalProfileDataUnavailableException ex)
        {
            if (!ct.IsCancellationRequested)
                await FailMissingLocalDataAsync(task, ex, ct);
            return task.Status;
        }
        catch (Exception ex)
        {
            return HandleTaskFailure(task, ex, ct);
        }
        finally
        {
            if (TryEnqueueRetry(task))
            {
                await EnsureProcessingTaskStartedAsync(CancellationToken.None);
                _queueSignal.Set();
            }
        }
    }

    private TransferTaskStatus HandleTaskFailure(TransferTask task, Exception ex, CancellationToken ct)
    {
        int currentFailures;
        lock (task)
        {
            // A cancelled request can still finish with an ordinary I/O error.
            if (!IsActiveTask(task) || ct.IsCancellationRequested)
                return task.Status;
            task.ErrorMessage = ex.Message;
            task.FailureCount++;
            currentFailures = Interlocked.Increment(ref _consecutiveFailures);
            task.Status = currentFailures >= 5 ? TransferTaskStatus.Failed : TransferTaskStatus.WaitForRetry;
            task.StartedTime = null;
            task.CompletedTime = DateTime.Now;
            if (task.Status == TransferTaskStatus.Failed)
                RemoveTask(task);
        }
        _logger.Write($"任务 {task.TaskId} 失败 (连续失败: {currentFailures}/5): {ex.Message}");
        if (currentFailures >= 5)
        {
            MarkAllQueuedTasksAsFailed();
            _logger.Write("队列因连续失败5次已停止");
        }
        NotifyStatusChanged(task);
        return task.Status;
    }

    private async Task FailMissingLocalDataAsync(TransferTask task, LocalProfileDataUnavailableException ex, CancellationToken ct)
    {
        if (task.Type == TransferType.Upload &&
            Profile.ParseProfileId(task.ProfileId, out var type, out var hash))
        {
            try
            {
                await _historyManager.HandleLocalFileUnavailableAsync(type, hash, ct);
            }
            catch (Exception persistException)
            {
                _logger.Write(
                    $"任务 {task.TaskId} 标记本地文件不可用失败: {persistException.Message}");
            }
        }
        lock (task)
        {
            if (!IsActiveTask(task))
                return;
            task.ErrorMessage = I18n.Strings.SourceFileMissingUnableToUpload;
            task.FailureCount++;
            task.Status = TransferTaskStatus.Failed;
            task.CompletedTime = DateTime.Now;
            RemoveTask(task);
        }
        _logger.Write($"任务 {task.TaskId} 因本地文件缺失失败: {ex.Message}");
        NotifyStatusChanged(task);
    }

    private bool TryEnqueueRetry(TransferTask task)
    {
        lock (_queueLock)
        {
            lock (task)
            {
                if (!IsActiveTask(task) || task.Status != TransferTaskStatus.WaitForRetry)
                    return false;
                if (!_isQueueStopped)
                {
                    _pendingTasks.Enqueue(task);
                    return true;
                }
                task.Status = TransferTaskStatus.Failed;
                task.ErrorMessage = "队列已停止";
                RemoveTask(task);
            }
        }
        NotifyStatusChanged(task);
        return false;
    }

    private async Task ExecuteTaskCoreAsync(TransferTask task, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        NotifyStatusChanged(task);
        if (task.Type == TransferType.Download)
            await ExecuteDownloadAsync(task, ct);
        else
            await ExecuteUploadAsync(task, ct);

        lock (task)
        {
            if (!IsActiveTask(task))
                return;
            task.Status = TransferTaskStatus.Completed;
            task.CompletedTime = DateTime.Now;
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            RemoveTask(task);
        }
        NotifyStatusChanged(task);
    }

    private async Task ExecuteDownloadAsync(TransferTask task, CancellationToken ct)
    {
        if (_remoteServerFactory.Current is not IOfficialSyncServer server)
            throw new InvalidOperationException("当前服务器不支持历史记录同步");
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
        if (_remoteServerFactory.Current is not IOfficialSyncServer server)
            throw new InvalidOperationException("当前服务器不支持历史记录同步");
        var profile = task.Profile;
        var record = await _historyManager.GetOrCreateHistoryRecord(profile, ct);
        var remote = await server.GetHistoryByProfileIdAsync(task.ProfileId, ct);
        ct.ThrowIfCancellationRequested();
        if (remote is { IsDeleted: false })
        {
            // Existing content needs no transfer; normal history events handle any pending property changes.
            if (profile.HasTransferData && await profile.IsDataComplete(false, ct))
            {
                var localRecord = await _historyManager.ToHistoryRecord(profile, ct);
                await _historyManager.UpdateHistoryLocalInfo(localRecord, ct);
            }
            await _historyManager.SyncRemoteHistoryAsync([remote], ct);
            return;
        }
        if (record.IsDeleted && !task.IsImmediateTask)
            return;

        var response = await UploadContentAsync(task, server, record, profile, ct);
        ct.ThrowIfCancellationRequested();
        await _historyManager.SyncRemoteHistoryAsync([response], ct);
    }

    private async Task<HistoryRecordDto> UploadContentAsync(
        TransferTask task, IOfficialSyncServer server, HistoryRecord record, Profile profile, CancellationToken ct)
    {
        var transferData = await profile.PrepareTransferData(_profileEnv.GetPersistentDir(), ct);
        var persistentInfo = await profile.Persist(_profileEnv.GetPersistentDir(), ct);
        record.FilePath = persistentInfo.FilePaths;
        record.TransferDataFile = persistentInfo.TransferDataFile;
        record.TransferDataHash = persistentInfo.TransferDataHash;
        record.IsLocalFileReady = true;
        await _historyManager.UpdateHistoryLocalInfo(record, ct);
        var dto = record.ToHistoryRecordDto();
        dto.IsDeleted = false;
        try
        {
            return await server.UploadHistoryAsync(dto, transferData, task.ProgressReporter, ct);
        }
        catch (RemoteHistoryConflictException ex) when (ex.ServerRecord is not null)
        {
            return ApplyUploadUpdate(dto, ex.ServerRecord);
        }
    }

    private static HistoryRecordDto ApplyUploadUpdate(HistoryRecordDto record, HistoryRecordUpdateDto update)
    {
        record.Starred = update.Starred ?? record.Starred;
        record.Pinned = update.Pinned ?? record.Pinned;
        record.IsDeleted = update.IsDelete ?? record.IsDeleted;
        record.Version = update.Version ?? record.Version;
        record.LastModified = update.LastModified ?? record.LastModified;
        record.LastAccessed = update.LastAccessed ?? record.LastAccessed;
        return record;
    }

    private void NotifyStatusChanged(TransferTask task)
    {
        TaskStatusChanged?.Invoke(this, task);
    }

    private bool IsActiveTask(TransferTask task) =>
        _activeTasks.TryGetValue(task.TaskId, out var current) && ReferenceEquals(current, task);

    private void RemoveTask(TransferTask task)
    {
        lock (task)
        {
            if (!_activeTasks.TryRemove(new KeyValuePair<string, TransferTask>(task.TaskId, task)))
                return;
            task.CancellationSource.Dispose();
            task.CompletionSource.TrySetResult(task.Status);
        }
    }

    private void MarkAllQueuedTasksAsFailed()
    {
        List<TransferTask> failed = [];
        lock (_queueLock)
        {
            _isQueueStopped = true;
            while (_pendingTasks.TryDequeue(out var task))
            {
                lock (task)
                {
                    if (!IsActiveTask(task) ||
                        (task.IsImmediateTask && task.Status == TransferTaskStatus.Pending) ||
                        task.Status is not (TransferTaskStatus.Pending or TransferTaskStatus.WaitForRetry))
                        continue;
                    task.Status = TransferTaskStatus.Failed;
                    task.ErrorMessage = "队列已停止";
                    RemoveTask(task);
                    failed.Add(task);
                }
            }
        }
        foreach (var task in failed)
            NotifyStatusChanged(task);
    }

    private void OnSyncHistoryChanged(RuntimeHistoryConfig config)
    {
        if (!config.EnableSyncHistory)
        {
            CancelNonImmediateTasks();
        }
    }

    private void CancelNonImmediateTasks() => CancelTasks(backgroundOnly: true);
}
