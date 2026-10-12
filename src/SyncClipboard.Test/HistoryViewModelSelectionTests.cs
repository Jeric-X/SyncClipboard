using ObservableCollections;
using SyncClipboard.Core.ViewModels.Sub;
using Moq;
using SyncClipboard.Core.Interfaces;
using System.Reflection;
using System.Runtime.CompilerServices;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.Utilities.Runner;
using System.Threading.Channels;

namespace SyncClipboard.Test;

[TestClass]
public class HistoryViewModelSelectionTests
{
    [TestMethod]
    public void ExportModalKeepsHistoryVisibleAcrossFocusChanges()
    {
        var viewModel = (HistoryViewModel)RuntimeHelpers.GetUninitializedObject(typeof(HistoryViewModel));
        var window = new Mock<IWindow>();
        typeof(HistoryViewModel).GetField("window", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(viewModel, window.Object);
        using (viewModel.HoldForModalOperation())
        {
            viewModel.OnGotFocus();
            viewModel.OnLostFocus();
            window.Verify(value => value.Hide(), Times.Never);
        }
    }

    [TestMethod]
    public async Task ReloadWaitsForCanceledPageBeforeResettingTheList()
    {
        var viewModel = (HistoryViewModel)RuntimeHelpers.GetUninitializedObject(typeof(HistoryViewModel));
        void SetField(string name, object value) => typeof(HistoryViewModel)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(viewModel, value);
        var runner = new SingletonTask();
        var releasePage = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var records = new ObservableList<HistoryRecordVM>();
        CancellationToken pageToken = default;
        var page = runner.Run(async token =>
        {
            pageToken = token;
            await releasePage.Task;
            records.Add((HistoryRecordVM)RuntimeHelpers.GetUninitializedObject(typeof(HistoryRecordVM)));
        });
        SetField("_loader", runner);
        SetField("_updateQueueCts", new CancellationTokenSource());
        SetField("allHistoryItems", records);
        SetField("selectedIndex", -1);
        var reload = (Task)typeof(HistoryViewModel).GetMethod("Reload", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(viewModel, null)!;
        Assert.IsTrue(pageToken.IsCancellationRequested);
        Assert.IsFalse(reload.IsCompleted);
        releasePage.SetResult();
        await page;
        await reload.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        Assert.IsEmpty(records);
        await StopUpdateQueueAsync(viewModel);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ScrollDoesNotStartAnotherPageBeforePendingReloadResetsTheList(bool supersedeReload)
    {
        var viewModel = (HistoryViewModel)RuntimeHelpers.GetUninitializedObject(typeof(HistoryViewModel));
        void SetField(string name, object? value) => typeof(HistoryViewModel)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(viewModel, value);
        var runner = new SingletonTask();
        var releasePage = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = runner.Run(_ => releasePage.Task);
        var records = new ObservableList<HistoryRecordVM> { (HistoryRecordVM)RuntimeHelpers.GetUninitializedObject(typeof(HistoryRecordVM)) };
        var logger = new Mock<ILogger>();
        SetField("_updateQueueCts", new CancellationTokenSource());
        SetField("allHistoryItems", records);
        SetField("selectedIndex", -1);
        SetField("window", Mock.Of<IWindow>());
        SetField("_loader", runner);
        SetField("logger", logger.Object);
        var context = new QueuedSynchronizationContext();
        var reload = StartReload(context);
        Task StartReload(SynchronizationContext reloadContext)
        {
            var original = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(reloadContext);
                return (Task)typeof(HistoryViewModel).GetMethod("Reload", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(viewModel, null)!;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(original);
            }
        }

        var latestContext = new QueuedSynchronizationContext();
        var latestReload = supersedeReload ? StartReload(latestContext) : reload;
        if (supersedeReload)
        {
            await context.ResumeNextAsync(TestContext.CancellationTokenSource.Token);
            await reload;
        }
        releasePage.SetResult();
        await page;

        // The old page has exited, but the latest reload has not reset the cursor/list.
        await viewModel.NotifyScrollPositionAsync(90, 10, 100);
        Assert.HasCount(1, records);
        logger.VerifyNoOtherCalls();

        SetField("window", null);
        await (supersedeReload ? latestContext : context).ResumeNextAsync(TestContext.CancellationTokenSource.Token);
        await latestReload.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        Assert.IsEmpty(records);
        logger.VerifyNoOtherCalls();
        await StopUpdateQueueAsync(viewModel);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReloadLogsFailureWithoutFaultingAndCanRetry(bool failBeforeReloadBody)
    {
        var viewModel = (HistoryViewModel)RuntimeHelpers.GetUninitializedObject(typeof(HistoryViewModel));
        void SetField(string name, object? value) => typeof(HistoryViewModel)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(viewModel, value);
        Task Reload() => (Task)typeof(HistoryViewModel).GetMethod("Reload", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(viewModel, null)!;
        var runner = new SingletonTask();
        var logger = new Mock<ILogger>();
        logger.Setup(value => value.WriteAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        var window = new Mock<IWindow>();
        window.Setup(value => value.ScrollToTop()).Throws(new InvalidOperationException("Reset failed"));
        SetField("_loader", runner);
        SetField("logger", logger.Object);
        SetField("window", window.Object);
        SetField("_updateQueueCts", new CancellationTokenSource());
        SetField("allHistoryItems", new ObservableList<HistoryRecordVM>());
        SetField("selectedIndex", -1);
        var releasePage = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = failBeforeReloadBody ? runner.Run(async token =>
        {
            using var registration = token.Register(() => throw new InvalidOperationException("Cancel callback failed"));
            await releasePage.Task;
        }) : Task.CompletedTask;

        try
        {
            await Reload().WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
            var expectedMessage = failBeforeReloadBody ? "Cancel callback failed" : "Reset failed";
            logger.Verify(value => value.WriteAsync("Failed to reload history:",
                It.Is<string>(message => message.Contains(expectedMessage) && message.Contains(nameof(InvalidOperationException)))), Times.Once);
        }
        finally
        {
            releasePage.TrySetResult();
            await page.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        }

        SetField("window", null);
        await Reload().WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        await StopUpdateQueueAsync(viewModel);
    }

    private async Task StopUpdateQueueAsync(HistoryViewModel viewModel)
    {
        var cancellation = (CancellationTokenSource)typeof(HistoryViewModel)
            .GetField("_updateQueueCts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(viewModel)!;
        cancellation.Cancel();
        await ((Task)typeof(HistoryViewModel).GetField("_queueConsumerTask", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(viewModel)!).WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        cancellation.Dispose();
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly Channel<Action> _continuations = Channel.CreateUnbounded<Action>();

        public override void Post(SendOrPostCallback callback, object? state)
        {
            _continuations.Writer.TryWrite(() => callback(state));
        }

        public async Task ResumeNextAsync(CancellationToken token)
        {
            var continuation = await _continuations.Reader.ReadAsync(token).AsTask().WaitAsync(TimeSpan.FromSeconds(5), token);
            continuation();
        }
    }

    [TestMethod]
    public void RemovingSelectedRecord_SelectsRecordOriginallyAfterIt()
    {
        var targetIndex = HistoryViewModel.GetSelectionTargetIndexBeforeRemoval(
            itemCount: 5,
            selectedIndex: 2,
            removedIndex: 2);

        Assert.AreEqual(3, targetIndex);
    }

    [TestMethod]
    public void RemovingLastSelectedRecord_SelectsRecordOriginallyBeforeIt()
    {
        var targetIndex = HistoryViewModel.GetSelectionTargetIndexBeforeRemoval(
            itemCount: 5,
            selectedIndex: 4,
            removedIndex: 4);

        Assert.AreEqual(3, targetIndex);
    }

    [TestMethod]
    public void RemovingDifferentRecord_PreservesSelectedRecord()
    {
        var targetIndex = HistoryViewModel.GetSelectionTargetIndexBeforeRemoval(
            itemCount: 5,
            selectedIndex: 3,
            removedIndex: 1);

        Assert.AreEqual(3, targetIndex);
    }

    public TestContext TestContext { get; set; } = null!;
}
