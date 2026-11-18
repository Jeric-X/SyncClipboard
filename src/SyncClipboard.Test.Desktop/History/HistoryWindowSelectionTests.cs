using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Threading;
using Moq;
using ObservableCollections;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.UserServices;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.ViewModels.Sub;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SyncClipboard.Test.Desktop.History;

[TestClass]
[DoNotParallelize]
public class HistoryWindowSelectionTests
{
    public TestContext TestContext { get; set; } = null!;

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public async Task ReopenAfterCopy_SelectsFirstRecordWithoutCollectionChanges(int copiedIndex)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryWindowSelectionTests));
        await session.Dispatch(() =>
        {
            using var fixture = new Fixture();
            fixture.Show();
            fixture.ViewModel.SelectedItem = fixture.Records[copiedIndex];
            Assert.AreSame(fixture.Records[copiedIndex], fixture.List.SelectedItem);

            // Copy and paste both clear the selection before hiding. The first record
            // stays in place, so no collection-change callback will restore selection.
            fixture.ViewModel.SelectedItem = null;
            fixture.Window.Hide();
            Assert.IsNull(fixture.List.SelectedItem);
            fixture.Show();

            Assert.AreSame(fixture.Records[0], fixture.ViewModel.SelectedItem);
            Assert.AreSame(fixture.Records[0], fixture.List.SelectedItem);
            Assert.AreSame(fixture.Records[0], fixture.ViewModel.PreviewedHistoryItem);
        }, TestContext.CancellationTokenSource.Token);
    }

    [TestMethod]
    [DataRow(false, 1)]
    [DataRow(true, 0)]
    public async Task ReopenWithExistingSelection_RespectsScrollToTopSetting(bool scrollToTop, int expectedIndex)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryWindowSelectionTests));
        await session.Dispatch(() =>
        {
            using var fixture = new Fixture(scrollToTop: scrollToTop);
            fixture.Show();
            fixture.ViewModel.SelectedItem = fixture.Records[1];
            fixture.Window.Hide();
            fixture.Show();
            Assert.AreSame(fixture.Records[expectedIndex], fixture.ViewModel.SelectedItem);
            Assert.AreSame(fixture.Records[expectedIndex], fixture.List.SelectedItem);
        }, TestContext.CancellationTokenSource.Token);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReopenWithoutSingleSelection_LeavesEmptyListAndMultiSelectUnselected(bool multiSelect)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryWindowSelectionTests));
        await session.Dispatch(() =>
        {
            using var fixture = new Fixture();
            if (multiSelect)
                SetField(fixture.ViewModel, "isMultiSelecting", true);
            else
                fixture.Records.Clear();
            fixture.Show();
            Assert.IsNull(fixture.ViewModel.SelectedItem);
            Assert.IsNull(fixture.List.SelectedItem);
        }, TestContext.CancellationTokenSource.Token);
    }

    private static void SetField(object target, string name, object value) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private sealed class MemoryConfig : ConfigBase
    {
        protected override bool Save() => true;
    }

    private sealed class Fixture : IDisposable
    {
        public HistoryViewModel ViewModel { get; } =
            (HistoryViewModel)RuntimeHelpers.GetUninitializedObject(typeof(HistoryViewModel));
        public ObservableList<HistoryRecordVM> Records { get; } = [];
        public ListBox List { get; } = new();
        public Window Window { get; }
        private readonly IDisposable view;
        private readonly NotifyCollectionChangedSynchronizedViewList<HistoryRecordVM> items;
        private readonly BindingExpressionBase binding;

        public Fixture(bool scrollToTop = false)
        {
            for (var index = 0; index < 2; index++)
            {
                var record = (HistoryRecordVM)RuntimeHelpers.GetUninitializedObject(typeof(HistoryRecordVM));
                record.Hash = index.ToString();
                Records.Add(record);
            }
            var synchronizedView = Records.CreateView(record => record);
            var historyItems = synchronizedView.ToNotifyCollectionChanged();
            view = synchronizedView;
            items = historyItems;
            var config = new MemoryConfig();
            config.SetConfig(new HistoryWindowConfig { ScrollToTopOnReopen = scrollToTop });
            SetField(ViewModel, "runtimeConfig", config);
            SetField(ViewModel, "<HistoryItems>k__BackingField", historyItems);
            SetField(ViewModel, "window", Mock.Of<IWindow>());
            SetField(ViewModel, "_foregroundWindowTrackingService", new ForegroundWindowTrackingService(
                Mock.Of<IForegroundWindowMonitor>(), Mock.Of<INativeWindowController>(), Mock.Of<ILogger>()));
            List.ItemsSource = historyItems;
            binding = List.Bind(ListBox.SelectedItemProperty, new Binding(nameof(HistoryViewModel.SelectedItem))
            {
                Source = ViewModel,
                Mode = BindingMode.TwoWay
            });
            Window = new Window { Content = List };
        }

        public void Show()
        {
            Window.Show();
            ViewModel.OnWindowShown();
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            Window.Close();
            binding.Dispose();
            items.Dispose();
            view.Dispose();
        }
    }
}
