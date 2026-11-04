using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ObservableCollections;
using SyncClipboard.Core;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.UserConfigs;
using System.Collections.ObjectModel;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Core.Utilities.Runner;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.ViewModels.Sub;
using SyncClipboard.Shared;
using SyncClipboard.Shared.Profiles;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SyncClipboard.Test.Client.History;

[TestClass]
[DoNotParallelize]
public class HistoryViewModelPagingTests
{
    private static readonly int[] ExpectedCombinedFilter = [12, 16];

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnavailableViewportPreloadsOnePageThenFillsAfterLayout(bool metricsAvailable)
    {
        await using var fixture = await Fixture.CreateAsync(TestContext.CancellationTokenSource.Token);
        fixture.MetricsAvailable = metricsAvailable;
        fixture.ViewportHeight = 0;

        await fixture.LoadAsync();
        Assert.HasCount(20, fixture.Records);

        // During layout an extent can arrive before a usable viewport.
        await fixture.ViewModel.NotifyScrollPositionAsync(0, 0, 20);
        Assert.HasCount(20, fixture.Records);

        fixture.MetricsAvailable = true;
        fixture.ViewportHeight = 50;
        await fixture.ViewModel.NotifyScrollPositionAsync(0, 50, 20);
        Assert.HasCount(60, fixture.Records);
        Assert.HasCount(60, fixture.Records.Select(record => record.Hash).Distinct().ToArray());
        fixture.Logger.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task FullViewportStopsPreloadingAndScrollLoadsNextPage()
    {
        await using var fixture = await Fixture.CreateAsync(TestContext.CancellationTokenSource.Token);
        fixture.MetricsAvailable = true;
        fixture.ViewportHeight = 10;

        await fixture.LoadAsync();
        Assert.HasCount(20, fixture.Records);
        await fixture.ViewModel.NotifyScrollPositionAsync(0, 10, 20);
        Assert.HasCount(20, fixture.Records);
        await fixture.ViewModel.NotifyScrollPositionAsync(10, 10, 20);
        Assert.HasCount(40, fixture.Records);
        fixture.Logger.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task TagFilter_RestoresPagesAndKeepsSelectionCountsAcrossFilterChanges()
    {
        await using var fixture = await Fixture.CreateAsync(TestContext.CancellationTokenSource.Token);
        await fixture.AddTagsAsync();
        fixture.RuntimeConfig.SetConfig(new HistoryWindowConfig { SelectedTags = ["Red"] });
        await fixture.LoadAsync();
        Assert.HasCount(20, fixture.Records);
        Assert.IsTrue(fixture.Records.All(record => int.Parse(record.Hash) % 2 == 0));
        await fixture.ViewModel.NotifyScrollPositionAsync(10, 10, 20);
        Assert.HasCount(40, fixture.Records);

        await fixture.ViewModel.SetCurrentFilterSelectionAsync(true);
        await fixture.RefreshCountsAsync();
        Assert.AreEqual(50, fixture.ViewModel.SelectedHistoryCount);
        Assert.AreEqual(50, fixture.ViewModel.SelectedInCurrentFilterCount);
        Assert.IsTrue(fixture.ViewModel.IsCurrentFilterFullySelected);

        var summaryChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(HistoryViewModel.IsCurrentFilterFullySelected)
                && fixture.ViewModel.IsCurrentFilterFullySelected is null)
                summaryChanged.TrySetResult();
        };
        await fixture.ChangeStoredTagsAsync(99, ["Red", "Blue"]);
        await summaryChanged.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
        Assert.HasCount(40, fixture.Records);
        Assert.AreEqual(50, fixture.ViewModel.SelectedInCurrentFilterCount);

        fixture.ViewModel.SelectedTags = ["Blue", "Red"];
        await fixture.ReloadAsync();
        await fixture.RefreshCountsAsync();
        Assert.HasCount(20, fixture.Records);
        Assert.IsTrue(fixture.Records.All(record => int.Parse(record.Hash) % 2 == 0 || int.Parse(record.Hash) % 3 == 0));
        Assert.IsNull(fixture.ViewModel.IsCurrentFilterFullySelected);
        await fixture.ViewModel.SetCurrentFilterSelectionAsync(true);
        Assert.AreEqual(67, fixture.ViewModel.SelectedHistoryCount);

        fixture.ViewModel.SelectedTags = [];
        await fixture.ReloadAsync();
        await fixture.ViewModel.SetCurrentFilterSelectionAsync(true);
        Assert.AreEqual(100, fixture.ViewModel.SelectedHistoryCount);
        fixture.Logger.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task TagFilter_LiveEditsEnterAndLeaveCombinedSearchAndStarredScope()
    {
        await using var fixture = await Fixture.CreateAsync(TestContext.CancellationTokenSource.Token);
        await fixture.AddTagsAsync();
        fixture.RuntimeConfig.SetConfig(new HistoryWindowConfig { SelectedTags = ["Red"], OnlyShowStarred = true });
        Fixture.SetField(fixture.ViewModel, "searchText", "record 1");
        await fixture.LoadAsync();
        CollectionAssert.AreEqual(ExpectedCombinedFilter, fixture.Records.Select(record => int.Parse(record.Hash)).ToArray());

        var removed = fixture.Records[0].DeepCopy();
        removed.Tags = ["red"];
        fixture.Publish(removed);
        Assert.HasCount(1, fixture.Records);
        removed.Tags = ["Red", "Blue"];
        fixture.Publish(removed);
        Assert.HasCount(2, fixture.Records);
        var updated = removed.DeepCopy();
        updated.Tags = ["Red"];
        fixture.Publish(updated);
        CollectionAssert.AreEqual(updated.Tags, fixture.Records[0].Tags);
        CollectionAssert.AreEqual(updated.Tags, fixture.Records[0].ToHistoryRecord().Tags);
        fixture.Logger.VerifyNoOtherCalls();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly HistoryDbContext db = new();
        private readonly SemaphoreSlim semaphore = new(1, 1);
        private readonly ServiceProvider services = new ServiceCollection()
            .AddSingleton(Mock.Of<IThreadDispatcher>())
            .AddSingleton(Mock.Of<IProfileEnv>()).BuildServiceProvider();
        private readonly AppCore? previousApp = AppCore.TryGetCurrent();
        public HistoryViewModel ViewModel { get; } = (HistoryViewModel)RuntimeHelpers.GetUninitializedObject(typeof(HistoryViewModel));
        public ObservableList<HistoryRecordVM> Records { get; } = [];
        public ConfigBase RuntimeConfig { get; } = CreateRuntimeConfig();
        public Mock<ILogger> Logger { get; } = new();
        public bool MetricsAvailable { get; set; }
        public double ViewportHeight { get; set; }

        private static ConfigBase CreateRuntimeConfig()
        {
            var path = Path.GetTempFileName();
            File.WriteAllText(path, "{}");
            return new ConfigBase(path);
        }

        private Fixture()
        {
            // Keep application startup, user configuration, and the real history database out of this test.
            var app = (AppCore)RuntimeHelpers.GetUninitializedObject(typeof(AppCore));
            SetField(app, "<Services>k__BackingField", services);
            typeof(AppCore).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, app);
            var manager = (HistoryManager)RuntimeHelpers.GetUninitializedObject(typeof(HistoryManager));
            SetField(manager, "_dbContext", db);
            SetField(manager, "_dbSemaphore", semaphore);
            var queue = (HistoryTransferQueue)RuntimeHelpers.GetUninitializedObject(typeof(HistoryTransferQueue));
            SetField(queue, "_activeTasks", new ConcurrentDictionary<string, TransferTask>());
            SetField(ViewModel, "historyManager", manager);
            SetField(ViewModel, "runtimeConfig", RuntimeConfig);
            SetField(ViewModel, "_transferQueue", queue);
            SetField(ViewModel, "allHistoryItems", Records);
            var view = Records.CreateView(record => record);
            SetField(ViewModel, "viewController", view);
            SetField(ViewModel, "<HistoryItems>k__BackingField", view.ToNotifyCollectionChanged());
            SetField(ViewModel, "_loader", new SingletonTask());
            SetField(ViewModel, "logger", Logger.Object);
            var dispatcher = new Mock<IThreadDispatcher>();
            dispatcher.Setup(value => value.RunOnMainThreadAsync(It.IsAny<Action>()))
                .Returns((Action action) =>
                {
                    action();
                    return Task.CompletedTask;
                });
            SetField(ViewModel, "_threadDispatcher", dispatcher.Object);
            var selection = typeof(HistoryViewModel).GetField("selectedHistoryRecords", BindingFlags.Instance | BindingFlags.NonPublic)!;
            selection.SetValue(ViewModel, Activator.CreateInstance(selection.FieldType));
            SetField(ViewModel, "visibleSelectedItems", new ObservableCollection<HistoryRecordVM>());
            typeof(HistoryViewModel).GetMethod("InitializeSelection", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(ViewModel, null);
            var window = new Mock<IWindow>();
            window.Setup(value => value.GetScrollViewMetrics(out It.Ref<double>.IsAny, out It.Ref<double>.IsAny,
                out It.Ref<double>.IsAny)).Returns((out double offset, out double viewport, out double extent) =>
                {
                    offset = 0;
                    viewport = ViewportHeight;
                    extent = Records.Count;
                    return MetricsAvailable;
                });
            SetField(ViewModel, "window", window.Object);
        }

        public static async Task<Fixture> CreateAsync(CancellationToken token)
        {
            var fixture = new Fixture();
            fixture.db.Database.SetConnectionString("Data Source=:memory:");
            await fixture.db.Database.OpenConnectionAsync(token);
            await fixture.db.Database.EnsureCreatedAsync(token);
            var now = DateTime.UtcNow;
            fixture.db.HistoryRecords.AddRange(Enumerable.Range(0, 100).Select(index => new HistoryRecord
            {
                Type = ProfileType.Text,
                Hash = index.ToString("D64"),
                Text = $"record {index}",
                Timestamp = now.AddMinutes(-index),
                LastAccessed = now.AddMinutes(-index),
            }));
            await fixture.db.SaveChangesAsync(token);
            return fixture;
        }

        public Task LoadAsync() => (Task)typeof(HistoryViewModel)
            .GetMethod("LoadPagesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ViewModel, [20, CancellationToken.None])!;

        public async Task AddTagsAsync()
        {
            foreach (var record in db.HistoryRecords)
            {
                var index = int.Parse(record.Hash);
                record.Tags = index % 2 == 0 ? index % 3 == 0 ? ["Red", "Blue"] : ["Red"]
                    : index % 3 == 0 ? ["Blue"] : ["red"];
                record.Stared = index % 4 == 0;
            }
            await db.SaveChangesAsync();
        }

        public async Task ChangeStoredTagsAsync(int index, string[] tags)
        {
            var record = await db.HistoryRecords.SingleAsync(record => record.Hash == index.ToString("D64"));
            record.Tags = tags;
            await db.SaveChangesAsync();
            typeof(HistoryViewModel).GetMethod("RecordEntityUpdated", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(ViewModel, [record]);
        }

        public Task ReloadAsync() => (Task)typeof(HistoryViewModel)
            .GetMethod("Reload", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(ViewModel, null)!;

        public Task RefreshCountsAsync()
        {
            var parts = typeof(HistoryViewModel).GetNestedType("SelectionSummaryPart", BindingFlags.NonPublic)!;
            return (Task)typeof(HistoryViewModel).GetMethod("RefreshSelectionSummaryAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(ViewModel, [Enum.Parse(parts, "Counts"), CancellationToken.None])!;
        }

        public void Publish(HistoryRecordVM record) => typeof(HistoryViewModel)
            .GetMethod("RecordUpdated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(ViewModel, [record]);

        public static void SetField(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

        public async ValueTask DisposeAsync()
        {
            typeof(AppCore).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, previousApp);
            var cancellation = (CancellationTokenSource?)typeof(HistoryViewModel)
                .GetField("_updateQueueCts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ViewModel);
            cancellation?.Cancel();
            var consumer = (Task?)typeof(HistoryViewModel)
                .GetField("_queueConsumerTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ViewModel);
            if (consumer is not null)
                await consumer;
            cancellation?.Dispose();
            await db.DisposeAsync();
            await services.DisposeAsync();
            semaphore.Dispose();
            File.Delete(RuntimeConfig.Path);
        }
    }
}
