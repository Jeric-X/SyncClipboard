using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ObservableCollections;
using SyncClipboard.Core;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Core.Utilities.Runner;
using SyncClipboard.Core.ViewModels;
using SyncClipboard.Core.ViewModels.Sub;
using SyncClipboard.Shared;
using SyncClipboard.Shared.Profiles;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SyncClipboard.Test;

[TestClass]
[DoNotParallelize]
public class HistoryViewModelPagingTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ExistingRecordStateUpdatesDoNotWaitForImageMetadata()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await Fixture.CreateAsync(token);
        var directory = Directory.CreateTempSubdirectory("history-metadata-updates-");
        var path = Path.Combine(directory.FullName, "image.png");
        try
        {
            using (var image = new MagickImage(MagickColors.Red, 640, 320))
                image.Write(path);
            await fixture.AddImageAsync(path, token);
            await fixture.LoadAsync();
            var record = fixture.Records.Single(item => item.Type == ProfileType.Image);
            record.PreviewImageMetadata = null;
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            record.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(HistoryRecordVM.PreviewImageMetadata) && record.PreviewImageMetadata is not null)
                    ready.TrySetResult();
            };

            // Occupy the metadata readers to simulate slow storage without platform-specific files.
            var slots = (SemaphoreSlim)typeof(HistoryImageMetadata)
                .GetField("ReadSlots", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            await slots.WaitAsync(token);
            await slots.WaitAsync(token);
            try
            {
                var update = record.ToHistoryRecord();
                update.LastAccessed = record.LastAccessed;
                update.Stared = true;
                fixture.NotifyRecordUpdated(update);
                Assert.IsTrue(record.Stared);

                update = record.ToHistoryRecord();
                update.LastAccessed = record.LastAccessed;
                update.Pinned = true;
                fixture.NotifyRecordUpdated(update);
                Assert.IsTrue(record.Stared && record.Pinned);
            }
            finally
            {
                slots.Release(2);
            }
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            Assert.IsTrue(record.Stared && record.Pinned);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ImageDimensionsArePreparedBeforePublishingHistoryRows(bool fileExists)
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await Fixture.CreateAsync(token);
        var directory = Directory.CreateTempSubdirectory("history-metadata-");
        var path = Path.Combine(directory.FullName, "image.png");
        try
        {
            if (fileExists)
            {
                using var image = new MagickImage(MagickColors.Red, 640, 320);
                image.Write(path);
            }
            await fixture.AddImageAsync(path, token);
            var metadataAtInsertion = new List<HistoryImageMetadata?>();
            using var view = fixture.Records.CreateView(record =>
            {
                if (record.Type == ProfileType.Image)
                    metadataAtInsertion.Add(record.PreviewImageMetadata);
                return record;
            });

            await fixture.LoadAsync();

            Assert.HasCount(20, fixture.Records);
            Assert.HasCount(1, metadataAtInsertion);
            if (fileExists)
                Assert.AreEqual(new HistoryImageMetadata(path, 640, 320, false), metadataAtInsertion[0]);
            else
                Assert.IsNull(metadataAtInsertion[0]);
            fixture.Logger.VerifyNoOtherCalls();
        }
        finally
        {
            directory.Delete(true);
        }
    }

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

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly HistoryDbContext db = new();
        private readonly SemaphoreSlim semaphore = new(1, 1);
        private readonly ServiceProvider services = new ServiceCollection()
            .AddSingleton(Mock.Of<IThreadDispatcher>())
            .AddSingleton(Mock.Of<IProfileEnv>(env => env.GetHistoryPersistentDir() == Path.GetTempPath())).BuildServiceProvider();
        private readonly AppCore? previousApp = AppCore.TryGetCurrent();
        public HistoryViewModel ViewModel { get; } = (HistoryViewModel)RuntimeHelpers.GetUninitializedObject(typeof(HistoryViewModel));
        public ObservableList<HistoryRecordVM> Records { get; } = [];
        public Mock<ILogger> Logger { get; } = new();
        public bool MetricsAvailable { get; set; }
        public double ViewportHeight { get; set; }

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
            SetField(ViewModel, "runtimeConfig", new ConfigBase());
            SetField(ViewModel, "_transferQueue", queue);
            SetField(ViewModel, "allHistoryItems", Records);
            SetField(ViewModel, "_loader", new SingletonTask());
            SetField(ViewModel, "logger", Logger.Object);
            var dispatcher = Mock.Get(services.GetRequiredService<IThreadDispatcher>());
            dispatcher.Setup(value => value.RunOnMainThreadAsync(It.IsAny<Action>())).Returns<Action>(action =>
            {
                action();
                return Task.CompletedTask;
            });
            SetField(ViewModel, "_threadDispatcher", dispatcher.Object);
            var selection = typeof(HistoryViewModel).GetField("selectedHistoryRecords", BindingFlags.Instance | BindingFlags.NonPublic)!;
            selection.SetValue(ViewModel, Activator.CreateInstance(selection.FieldType));
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

        public void NotifyRecordUpdated(HistoryRecord record) => typeof(HistoryViewModel)
            .GetMethod("RecordEntityUpdated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(ViewModel, [record]);

        public async Task AddImageAsync(string path, CancellationToken token)
        {
            db.HistoryRecords.Add(new HistoryRecord
            {
                Type = ProfileType.Image,
                Hash = new string('a', 64),
                FilePath = [path],
                IsLocalFileReady = true,
                Timestamp = DateTime.UtcNow,
                LastAccessed = DateTime.UtcNow
            });
            await db.SaveChangesAsync(token);
        }

        private static void SetField(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

        public async ValueTask DisposeAsync()
        {
            typeof(AppCore).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, previousApp);
            await db.DisposeAsync();
            await services.DisposeAsync();
            semaphore.Dispose();
        }
    }
}
