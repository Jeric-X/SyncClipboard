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
}
