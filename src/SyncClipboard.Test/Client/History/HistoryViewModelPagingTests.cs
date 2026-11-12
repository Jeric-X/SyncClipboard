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
    private static readonly string[] InitialRedFilter = ["Red"];

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
    public async Task TagFilter_PagesAndKeepsSelectionCountsAcrossFilterChanges()
    {
        await using var fixture = await Fixture.CreateAsync(TestContext.CancellationTokenSource.Token);
        await fixture.AddTagsAsync();
        Fixture.SetField(fixture.ViewModel, "_selectedTags", InitialRedFilter);
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
        fixture.RuntimeConfig.SetConfig(new HistoryWindowConfig { OnlyShowStarred = true });
        Fixture.SetField(fixture.ViewModel, "_selectedTags", InitialRedFilter);
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

    [TestMethod]
    public async Task TagBar_SelectsCancelsAndClearsFilterWhenHidden()
    {
        await using var fixture = await Fixture.CreateAsync(TestContext.CancellationTokenSource.Token);
        await fixture.AddTagsAsync();
        fixture.InitializeTags();
        await fixture.ViewModel.RefreshTagOptionsAsync();
        var red = fixture.ViewModel.TagOptions.Single(option => option.Name == "Red");
        Assert.AreEqual("Red", red.Text);
        fixture.ViewModel.ShowTagCounts = true;
        Assert.AreEqual("Red (50)", red.Text);
        Assert.AreEqual("Blue (34)", fixture.ViewModel.TagOptions.Single(option => option.Name == "Blue").Text);

        red.SelectCommand.Execute(null);
        await fixture.ReloadAsync();
        Assert.IsTrue(fixture.Records.All(record => record.Tags.Contains("Red")));
        Assert.IsTrue(red.IsSelected);
        red.SelectCommand.Execute(null);
        await fixture.ReloadAsync();
        Assert.IsTrue(fixture.Records.Any(record => !record.Tags.Contains("Red")));
        Assert.IsTrue(fixture.ViewModel.TagOptions.Single(option => option.Name is null).IsSelected);

        red.SelectCommand.Execute(null);
        await fixture.ReloadAsync();
        fixture.ViewModel.ShowTagBar = false;
        await fixture.ReloadAsync();
        await fixture.ViewModel.SetCurrentFilterSelectionAsync(true);
        Assert.AreEqual(100, fixture.ViewModel.SelectedHistoryCount);
        Assert.IsFalse(fixture.ViewModel.IsTagBarVisible);
        var reloaded = new ConfigBase(fixture.RuntimeConfig.Path).GetConfig<HistoryWindowConfig>();
        Assert.IsFalse(reloaded.ShowTagBar);
        Assert.IsTrue(reloaded.ShowTagCounts);
        fixture.ViewModel.ShowTagBar = true;
        Assert.IsTrue(fixture.ViewModel.IsTagBarVisible);
        Assert.IsTrue(fixture.ViewModel.TagOptions.Single(option => option.Name is null).IsSelected);
        fixture.Logger.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task TagBar_MultipleSelectionCombinesTagsAndRestoresSingleSelection()
    {
        await using var fixture = await Fixture.CreateAsync(TestContext.CancellationTokenSource.Token);
        await fixture.AddTagsAsync();
        fixture.InitializeTags();
        await fixture.ViewModel.RefreshTagOptionsAsync();
        var red = fixture.ViewModel.TagOptions.Single(option => option.Name == "Red");
        var blue = fixture.ViewModel.TagOptions.Single(option => option.Name == "Blue");
        var all = fixture.ViewModel.TagOptions.Single(option => option.Name is null);

        fixture.ViewModel.MultiTagFilter = true;
        red.IsSelected = true;
        red.SelectCommand.Execute(null);
        blue.IsSelected = true;
        blue.SelectCommand.Execute(null);
        await fixture.ReloadAsync();
        await fixture.ViewModel.SetCurrentFilterSelectionAsync(true);
        Assert.AreEqual(67, fixture.ViewModel.SelectedHistoryCount);
        Assert.IsTrue(fixture.Records.All(record => int.Parse(record.Hash) % 2 == 0 || int.Parse(record.Hash) % 3 == 0));
        Assert.IsTrue(red.IsSelected && blue.IsSelected);
        Assert.IsFalse(all.IsSelected);
        var reloaded = new ConfigBase(fixture.RuntimeConfig.Path);
        Fixture.SetField(fixture.ViewModel, "runtimeConfig", reloaded);
        Assert.IsTrue(fixture.ViewModel.MultiTagFilter);
        Assert.HasCount(2, fixture.ViewModel.SelectedTags);

        blue.IsSelected = false;
        blue.SelectCommand.Execute(null);
        await fixture.ReloadAsync();
        await fixture.RefreshCountsAsync();
        Assert.AreEqual(50, fixture.ViewModel.SelectedInCurrentFilterCount);
        Assert.IsTrue(fixture.Records.All(record => int.Parse(record.Hash) % 2 == 0));
        Assert.IsTrue(red.IsSelected);
        Assert.IsFalse(blue.IsSelected);

        blue.IsSelected = true;
        blue.SelectCommand.Execute(null);
        await fixture.ViewModel.SetPinnedTagsAsync(["red", "Red"]);
        fixture.ViewModel.MultiTagFilter = false;
        await fixture.ReloadAsync();
        await fixture.RefreshCountsAsync();
        Assert.HasCount(1, fixture.ViewModel.SelectedTags);
        Assert.AreEqual("Red", fixture.ViewModel.SelectedTags[0]);
        Assert.AreEqual(50, fixture.ViewModel.SelectedInCurrentFilterCount);
        Assert.IsTrue(fixture.Records.All(record => record.Tags.Contains("Red")));
        Assert.IsFalse(new ConfigBase(fixture.RuntimeConfig.Path).GetConfig<HistoryWindowConfig>().MultiTagFilter);

        all.IsSelected = true;
        all.SelectCommand.Execute(null);
        await fixture.ReloadAsync();
        await fixture.ViewModel.SetCurrentFilterSelectionAsync(true);
        Assert.AreEqual(100, fixture.ViewModel.SelectedHistoryCount);
        Assert.IsEmpty(fixture.ViewModel.SelectedTags);
        Assert.IsTrue(all.IsSelected);

        fixture.ViewModel.MultiTagFilter = true;
        red.SelectCommand.Execute(null);
        await fixture.ReloadAsync();
        var savedConfig = new ConfigBase(fixture.RuntimeConfig.Path).GetConfig<HistoryWindowConfig>();
        await using var restarted = await Fixture.CreateAsync(TestContext.CancellationTokenSource.Token);
        restarted.RuntimeConfig.SetConfig(savedConfig);
        await restarted.LoadAsync();
        await restarted.ViewModel.SetCurrentFilterSelectionAsync(true);
        Assert.AreEqual(100, restarted.ViewModel.SelectedHistoryCount);
        Assert.IsTrue(restarted.ViewModel.MultiTagFilter);
        restarted.Logger.VerifyNoOtherCalls();
        fixture.Logger.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task TagBar_HistoryUpdatesPreserveSelectedItemsAndUpdateCountsInPlace()
    {
        await using var fixture = await Fixture.CreateAsync(TestContext.CancellationTokenSource.Token);
        await fixture.AddTagsAsync();
        fixture.InitializeTags();
        fixture.ViewModel.ShowTagCounts = true;
        await fixture.ViewModel.RefreshTagOptionsAsync();
        var options = fixture.ViewModel.TagOptions;
        var all = options[0];
        var red = options.Single(option => option.Name == "Red");
        var blue = options.Single(option => option.Name == "Blue");
        red.SelectCommand.Execute(null);
        await fixture.ReloadAsync();
        var selectionChanges = 0;
        red.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(HistoryTagFilter.IsSelected))
                selectionChanges++;
        };
        var textChanges = 0;
        red.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(HistoryTagFilter.Text))
                textChanges++;
        };
        var collectionChanges = 0;
        options.CollectionChanged += (_, _) => collectionChanges++;

        await fixture.ViewModel.RefreshTagOptionsAsync();
        Assert.AreEqual(0, collectionChanges);
        await fixture.ChangeStoredTagsAsync(2, ["Blue"]);
        await fixture.ViewModel.RefreshTagOptionsAsync();
        Assert.AreSame(options, fixture.ViewModel.TagOptions);
        Assert.AreSame(red, options.Single(option => option.Name == "Red"));
        Assert.AreEqual("Red (49)", red.Text);
        Assert.AreEqual("Blue (35)", blue.Text);
        Assert.IsGreaterThan(0, textChanges);
        Assert.AreEqual(0, collectionChanges);

        await fixture.ReplaceTagsAsync("Blue", "Amber");
        await fixture.ViewModel.RefreshTagOptionsAsync();
        Assert.AreSame(all, options[0]);
        Assert.AreSame(red, options.Single(option => option.Name == "Red"));
        Assert.AreEqual("Amber (35)", options[1].Text);
        Assert.DoesNotContain(blue, options);
        Assert.IsTrue(red.IsSelected);
        Assert.AreEqual(0, selectionChanges);
        Assert.AreEqual(2, collectionChanges);
        fixture.Logger.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task TagBar_RefreshCoalescesChangesAndRetainsAnEmptySelectedTag()
    {
        await using var fixture = await Fixture.CreateAsync(TestContext.CancellationTokenSource.Token);
        await fixture.AddTagsAsync();
        fixture.InitializeTags();
        await fixture.ViewModel.RefreshTagOptionsAsync();
        fixture.ViewModel.ShowTagCounts = true;
        fixture.ViewModel.TagOptions.Single(option => option.Name == "red").SelectCommand.Execute(null);
        await fixture.ReloadAsync();
        var firstResultReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstResult = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publications = 0;
        fixture.Dispatcher.Setup(value => value.RunOnMainThreadAsync(It.IsAny<Action>())).Returns(async (Action action) =>
        {
            if (Interlocked.Increment(ref publications) == 1)
            {
                firstResultReady.SetResult();
                await releaseFirstResult.Task;
            }
            action();
        });
        var refreshes = new List<Task>();
        try
        {
            for (var i = 0; i < 100; i++)
                refreshes.Add(fixture.ViewModel.RefreshTagOptionsAsync());
            await firstResultReady.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
            await fixture.ReplaceTagsAsync("red", "new");
            for (var i = 0; i < 100; i++)
                refreshes.Add(fixture.ViewModel.RefreshTagOptionsAsync());
            releaseFirstResult.SetResult();
            await Task.WhenAll(refreshes).WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationTokenSource.Token);
            Assert.AreEqual(2, publications);
            Assert.AreEqual("new (33)", fixture.ViewModel.TagOptions.Single(option => option.Name == "new").Text);
            var selected = fixture.ViewModel.TagOptions.Single(option => option.Name == "red");
            Assert.AreEqual("red (0)", selected.Text);
            Assert.IsTrue(selected.IsSelected);
            selected.SelectCommand.Execute(null);
            await fixture.ReloadAsync();
            Assert.HasCount(20, fixture.Records);
            fixture.Logger.VerifyNoOtherCalls();
        }
        finally
        {
            releaseFirstResult.TrySetResult();
            await Task.WhenAll(refreshes);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ContextMenu_StarExportAndDeleteUseTheCurrentSelectionScope(bool multiple)
    {
        await using var fixture = await Fixture.CreateAsync(TestContext.CancellationTokenSource.Token);
        await fixture.LoadAsync();
        var clicked = fixture.Records[0];
        var targets = multiple ? new[] { fixture.Records[1], fixture.Records[2] } : [clicked];
        if (multiple)
        {
            var (_, enterCommand, enterParameter) = fixture.ViewModel.BuildHistoryMenuCommands(targets[0])
                .Single(item => item.Text == SyncClipboard.Core.I18n.Strings.HistoryMultiSelectEnter);
            enterCommand.Execute(enterParameter);
            Assert.IsTrue(fixture.ViewModel.IsMultiSelecting);
            CollectionAssert.AreEqual(new[] { targets[0].Key }, fixture.ViewModel.GetTagEditSelection());
            fixture.ViewModel.EnterMultiSelect(targets[1]);
        }
        var commands = fixture.ViewModel.BuildHistoryMenuCommands(clicked);
        var (_, starCommand, starParameter) = commands.Single(item => item.Text == SyncClipboard.Core.I18n.Strings.HistoryMultiSelectStar);
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)starCommand).ExecuteAsync(starParameter);
        foreach (var record in targets)
            Assert.IsTrue((await fixture.ReadStoredRecordAsync(record)).Stared);
        var untouched = multiple ? clicked : fixture.Records[1];
        Assert.IsFalse((await fixture.ReadStoredRecordAsync(untouched)).Stared);

        IReadOnlyList<HistoryRecordKey>? exported = null;
        fixture.ExportDialog.Setup(dialog => dialog.ShowAsync(It.IsAny<IReadOnlyList<HistoryRecordKey>?>(), true))
            .Callback<IReadOnlyList<HistoryRecordKey>?, bool>((keys, _) => exported = keys)
            .Returns(Task.CompletedTask);
        var (_, exportCommand, exportParameter) = commands.Single(item => item.Text == SyncClipboard.Core.I18n.Strings.HistoryExportSelected);
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)exportCommand).ExecuteAsync(exportParameter);
        Assert.IsNotNull(exported);
        CollectionAssert.AreEquivalent(targets.Select(record => record.Key).ToArray(), exported.ToArray());

        var (_, deleteCommand, deleteParameter) = commands.Single(item => item.Text == SyncClipboard.Core.I18n.Strings.HistoryMultiSelectDelete);
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)deleteCommand).ExecuteAsync(deleteParameter);
        foreach (var record in targets)
            Assert.IsTrue((await fixture.ReadStoredRecordAsync(record)).IsDeleted);
        Assert.IsFalse((await fixture.ReadStoredRecordAsync(untouched)).IsDeleted);
        fixture.ConfirmationDialog.Verify(dialog => dialog.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>()),
            multiple ? Times.Exactly(2) : Times.Never());
        fixture.Logger.VerifyNoOtherCalls();
    }

    public sealed class Fixture : IAsyncDisposable
    {
        private readonly HistoryDbContext db = new();
        private readonly SemaphoreSlim semaphore = new(1, 1);
        private readonly ServiceProvider services;
        public Mock<IHistoryExportDialog> ExportDialog { get; } = new();
        public Mock<IMainWindowDialog> ConfirmationDialog { get; } = new();
        private readonly AppCore? previousApp = AppCore.TryGetCurrent();
        public HistoryManager Manager { get; } = (HistoryManager)RuntimeHelpers.GetUninitializedObject(typeof(HistoryManager));
        public HistoryViewModel ViewModel { get; } = (HistoryViewModel)RuntimeHelpers.GetUninitializedObject(typeof(HistoryViewModel));
        public ObservableList<HistoryRecordVM> Records { get; } = [];
        public ConfigBase RuntimeConfig { get; } = CreateRuntimeConfig();
        public Mock<IThreadDispatcher> Dispatcher { get; } = new();
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
            var profileEnv = Mock.Of<IProfileEnv>(env => env.GetHistoryPersistentDir() == Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
            ConfirmationDialog.Setup(dialog => dialog.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            services = new ServiceCollection()
                .AddSingleton(Mock.Of<IThreadDispatcher>())
                .AddSingleton(profileEnv)
                .AddSingleton(ExportDialog.Object)
                .AddKeyedSingleton<IMainWindowDialog>("HistoryWindow", ConfirmationDialog.Object)
                .BuildServiceProvider();
            var app = (AppCore)RuntimeHelpers.GetUninitializedObject(typeof(AppCore));
            SetField(app, "<Services>k__BackingField", services);
            typeof(AppCore).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, app);
            SetField(Manager, "_dbContext", db);
            SetField(Manager, "_dbSemaphore", semaphore);
            SetField(Manager, "_profileEnv", profileEnv);
            SetField(Manager, "_runtimeHistoryConfig", new RuntimeHistoryConfig());
            var queue = (HistoryTransferQueue)RuntimeHelpers.GetUninitializedObject(typeof(HistoryTransferQueue));
            SetField(queue, "_activeTasks", new ConcurrentDictionary<string, TransferTask>());
            SetField(ViewModel, "historyManager", Manager);
            SetField(ViewModel, "_serviceProvider", services);
            SetField(ViewModel, "runtimeConfig", RuntimeConfig);
            ViewModel.TagOptions = [];
            SetField(ViewModel, "_selectedTags", Array.Empty<string>());
            SetField(ViewModel, "_transferQueue", queue);
            SetField(ViewModel, "allHistoryItems", Records);
            var view = Records.CreateView(record => record);
            SetField(ViewModel, "viewController", view);
            SetField(ViewModel, "<HistoryItems>k__BackingField", view.ToNotifyCollectionChanged());
            SetField(ViewModel, "_loader", new SingletonTask());
            SetField(ViewModel, "logger", Logger.Object);
            Dispatcher.Setup(value => value.RunOnMainThreadAsync(It.IsAny<Action>()))
                .Returns((Action action) =>
                {
                    action();
                    return Task.CompletedTask;
                });
            SetField(ViewModel, "_threadDispatcher", Dispatcher.Object);
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

        public Task<HistoryRecord> ReadStoredRecordAsync(HistoryRecordVM record) =>
            db.HistoryRecords.AsNoTracking().SingleAsync(item => item.Hash == record.Hash && item.Type == record.Type);

        public Task LoadAsync() => (Task)typeof(HistoryViewModel)
            .GetMethod("LoadPagesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ViewModel, [20, CancellationToken.None])!;

        public void InitializeTags() => typeof(HistoryViewModel)
            .GetMethod("InitializeTagRefresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(ViewModel, null);

        public async Task ReplaceTagsAsync(string oldTag, string newTag)
        {
            foreach (var record in db.HistoryRecords.Local)
                record.Tags = record.Tags.Select(tag => tag == oldTag ? newTag : tag).ToArray();
            await db.SaveChangesAsync();
        }

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
            await ViewModel.RefreshTagOptionsAsync();
            await db.DisposeAsync();
            await services.DisposeAsync();
            semaphore.Dispose();
            File.Delete(RuntimeConfig.Path);
        }
    }
}
