using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Threading;
using Moq;
using SharpHook.Simulation;
using ObservableCollections;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Models.Permissions;
using SyncClipboard.Core.Utilities.Keyboard;
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
    [DataRow(0, false, false)]
    [DataRow(1, false, false)]
    [DataRow(0, true, false)]
    [DataRow(1, true, false)]
    [DataRow(0, false, true)]
    [DataRow(1, false, true)]
    [DataRow(0, true, true)]
    [DataRow(1, true, true)]
    public async Task CopyOrPaste_KeepsSelectionAndOnlyScrollsOnReopenWhenEnabled(
        int copiedIndex, bool paste, bool scrollToTop)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(HistoryWindowSelectionTests));
        await session.Dispatch(async () =>
        {
            using var fixture = new Fixture(scrollToTop);
            fixture.Show();
            var copied = fixture.Records[copiedIndex];
            fixture.ViewModel.SelectedItem = copied;
            fixture.WindowApi.Invocations.Clear();

            await fixture.HideAfterCopyAsync(paste, TestContext.CancellationTokenSource.Token);

            Assert.IsFalse(fixture.Window.IsVisible);
            Assert.AreSame(copied, fixture.ViewModel.SelectedItem);
            Assert.AreSame(copied, fixture.List.SelectedItem);
            fixture.WindowApi.Verify(window => window.ScrollToTop(), Times.Never);
            fixture.WindowApi.Verify(window => window.ScrollToSelectedItem(), Times.Never);
            fixture.Show();

            var expected = scrollToTop ? fixture.Records[0] : copied;
            Assert.AreSame(expected, fixture.ViewModel.SelectedItem);
            Assert.AreSame(expected, fixture.List.SelectedItem);
            Assert.AreSame(expected, fixture.ViewModel.PreviewedHistoryItem);
            fixture.WindowApi.Verify(window => window.ScrollToSelectedItem(), scrollToTop ? Times.Once : Times.Never);
            fixture.WindowApi.Verify(window => window.ScrollToTop(), Times.Never);
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
        public Mock<IWindow> WindowApi { get; } = new();
        private readonly VirtualKeyboard keyboard;
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
            config.SetConfig(new HistoryWindowConfig { ScrollToTopOnReopen = scrollToTop, PasteDelayAfterWindowHiddenMilliseconds = 0 });
            SetField(ViewModel, "runtimeConfig", config);
            SetField(ViewModel, "<HistoryItems>k__BackingField", historyItems);
            SetField(ViewModel, "window", WindowApi.Object);
            SetField(ViewModel, "logger", Mock.Of<ILogger>());
            var permissions = new Mock<IPermissionProvider>();
            permissions.Setup(value => value.GetState(PermissionCapability.SimulateInput)).Returns(PermissionState.Available);
            keyboard = new VirtualKeyboard(permissions.Object);
            SetField(keyboard, "_simulator", Mock.Of<IEventSimulator>());
            SetField(ViewModel, "keyboard", keyboard);
            SetField(ViewModel, "_foregroundWindowTrackingService", new ForegroundWindowTrackingService(
                Mock.Of<IForegroundWindowMonitor>(), Mock.Of<INativeWindowController>(), Mock.Of<ILogger>()));
            List.ItemsSource = historyItems;
            binding = List.Bind(ListBox.SelectedItemProperty, new Binding(nameof(HistoryViewModel.SelectedItem))
            {
                Source = ViewModel,
                Mode = BindingMode.TwoWay
            });
            Window = new Window { Content = List };
            WindowApi.Setup(window => window.Hide()).Callback(Window.Hide);
        }

        public Task HideAfterCopyAsync(bool paste, CancellationToken token)
        {
            if (paste)
                return (Task)typeof(HistoryViewModel)
                    .GetMethod("PasteToLastExternalWindowAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(ViewModel, [token])!;

            typeof(HistoryViewModel)
                .GetMethod("CloseHistoryWindowAfterCopyIfNeeded", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(ViewModel, null);
            return Task.CompletedTask;
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
            keyboard.Dispose();
            binding.Dispose();
            items.Dispose();
            view.Dispose();
        }
    }
}
