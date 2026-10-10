using ObservableCollections;
using SyncClipboard.Core.ViewModels.Sub;
using Moq;
using SyncClipboard.Core.Interfaces;
using System.Reflection;
using System.Runtime.CompilerServices;
using SyncClipboard.Core.ViewModels;

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
        var previousLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var previousCancellation = new CancellationTokenSource();
        var records = new ObservableList<HistoryRecordVM> { (HistoryRecordVM)RuntimeHelpers.GetUninitializedObject(typeof(HistoryRecordVM)) };
        SetField("_activePageLoad", previousLoad.Task);
        SetField("_loadCts", previousCancellation);
        SetField("allHistoryItems", records);
        SetField("selectedIndex", -1);
        var reload = (Task)typeof(HistoryViewModel).GetMethod("Reload", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(viewModel, null)!;
        Assert.IsTrue(previousCancellation.IsCancellationRequested);
        Assert.IsFalse(reload.IsCompleted);
        Assert.HasCount(1, records);
        previousLoad.SetResult();
        await reload.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        Assert.IsEmpty(records);
        var cancellation = (CancellationTokenSource)typeof(HistoryViewModel)
            .GetField("_loadCts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(viewModel)!;
        cancellation.Cancel();
        await ((Task)typeof(HistoryViewModel).GetField("_queueConsumerTask", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(viewModel)!).WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        cancellation.Dispose();
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

    public TestContext TestContext { get; set; }
}
