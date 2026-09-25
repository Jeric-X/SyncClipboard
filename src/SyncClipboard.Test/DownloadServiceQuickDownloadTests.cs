using CommunityToolkit.Mvvm.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NativeNotification.Interface;
using SharpHook.Data;
using SharpHook.Simulation;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.RemoteServer;
using SyncClipboard.Core.RemoteServer.Adapter;
using SyncClipboard.Core.UserServices;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Core.Utilities.History;
using SyncClipboard.Core.Utilities.Keyboard;
using SyncClipboard.Shared;
using SyncClipboard.Shared.Profiles;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SyncClipboard.Test;

[TestClass]
[DoNotParallelize]
public class DownloadServiceQuickDownloadTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task QuickDownload_WithAutoSyncDisabled_ShouldApplyRemoteClipboard(
        bool copyLocallyBetweenDownloads, bool changeRemoteBetweenDownloads)
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await Fixture.CreateAsync(token);

        await fixture.DownloadAsync(token);
        Assert.AreEqual("remote first", fixture.LocalText, "The first manual download must succeed.");
        Assert.AreEqual(1, fixture.ClipboardWrites);

        if (copyLocallyBetweenDownloads)
        {
            fixture.CopyLocally("local copied after first download");
        }
        if (changeRemoteBetweenDownloads)
        {
            fixture.RemoteText = "remote second";
        }

        await fixture.DownloadAsync(token);

        // Verify the command reached the server, so an unchanged clipboard cannot be attributed to a lost hotkey.
        fixture.Adapter.Verify(adapter => adapter.GetProfileAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.AreEqual(fixture.RemoteText, fixture.LocalText,
            "Issue #442: a completed manual download must replace content copied before the command.");
        Assert.AreEqual(copyLocallyBetweenDownloads || changeRemoteBetweenDownloads ? 2 : 1,
            fixture.ClipboardWrites);
    }

    [TestMethod]
    [DataRow(false, false, false)]
    [DataRow(false, true, false)]
    [DataRow(true, false, false)]
    [DataRow(true, true, false)]
    [DataRow(false, false, true)]
    [DataRow(false, true, true)]
    [DataRow(true, false, true)]
    [DataRow(true, true, true)]
    public async Task QuickDownload_WhenLocalClipboardChangesDuringDownload_ShouldPreserveItAndAllowRetry(
        bool hasPreviousDownload, bool copyJustBeforeWrite, bool paste)
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await Fixture.CreateAsync(token);
        if (hasPreviousDownload)
        {
            await fixture.DownloadAsync(token);
        }

        fixture.CopyLocally("local before command");
        fixture.RemoteText = "remote pending";
        var writesBeforeDownload = fixture.ClipboardWrites;
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Adapter.Setup(adapter => adapter.GetProfileAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(async cancellationToken =>
            {
                requested.TrySetResult();
                await resume.Task.WaitAsync(cancellationToken);
                return await new TextProfile(fixture.RemoteText).ToProfileDto(cancellationToken);
            });
        var download = fixture.DownloadAsync(token, paste);
        try
        {
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            if (copyJustBeforeWrite)
            {
                fixture.BeforeClipboardWrite = () => fixture.CopyLocally("local copied during download");
            }
            else
            {
                fixture.CopyLocally("local copied during download");
            }
        }
        finally
        {
            resume.TrySetResult();
            await download;
            fixture.BeforeClipboardWrite = null;
        }

        Assert.AreEqual("local copied during download", fixture.LocalText);
        Assert.AreEqual(writesBeforeDownload, fixture.ClipboardWrites,
            "A download must not overwrite content copied after the command started.");
        fixture.VerifySkippedWrite();
        Assert.IsEmpty(fixture.PastedTexts, "A skipped download must not paste the newly copied local content.");

        // A skipped write must not leave a stale baseline that blocks the user's next explicit download.
        await fixture.DownloadAsync(token, paste);
        Assert.AreEqual("remote pending", fixture.LocalText);
        Assert.AreEqual(writesBeforeDownload + 1, fixture.ClipboardWrites);
        string[] expected = paste ? ["remote pending"] : [];
        CollectionAssert.AreEqual(expected, fixture.PastedTexts);
    }

    [TestMethod]
    public async Task QuickDownloadAndPaste_WhenLocalAlreadyMatchesRemote_ShouldPasteWithoutWriting()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await Fixture.CreateAsync(token);
        fixture.CopyLocally(fixture.RemoteText);

        await fixture.DownloadAsync(token, paste: true);

        Assert.AreEqual(0, fixture.ClipboardWrites);
        CollectionAssert.AreEqual(new[] { fixture.RemoteText }, fixture.PastedTexts);
    }

    [TestMethod]
    public async Task QuickDownloadAndPaste_WhenClipboardWriteFails_ShouldNotPasteAndAllowRetry()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await Fixture.CreateAsync(token);
        fixture.ClipboardWriteException = new InvalidOperationException("Clipboard write error");

        await fixture.DownloadAsync(token, paste: true);

        Assert.IsEmpty(fixture.PastedTexts);
        Assert.AreEqual(0, fixture.ClipboardWrites);
        Assert.AreEqual("local initial", fixture.LocalText);

        fixture.ClipboardWriteException = null;
        await fixture.DownloadAsync(token, paste: true);
        Assert.AreEqual(1, fixture.ClipboardWrites);
        CollectionAssert.AreEqual(new[] { fixture.RemoteText }, fixture.PastedTexts);
    }

    [TestMethod]
    public async Task QuickDownload_WhenAnotherCommandStarts_ShouldCancelPendingRemoteRequest()
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await Fixture.CreateAsync(token);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken firstRequestToken = default;
        var requests = 0;
        fixture.Adapter.Setup(adapter => adapter.GetProfileAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(async cancellationToken =>
            {
                if (Interlocked.Increment(ref requests) == 1)
                {
                    firstRequestToken = cancellationToken;
                    requested.TrySetResult();
                    await resume.Task.WaitAsync(cancellationToken);
                    return await new TextProfile("remote superseded").ToProfileDto(cancellationToken);
                }
                return await new TextProfile("remote latest").ToProfileDto(cancellationToken);
            });

        var firstDownload = fixture.DownloadAsync(token);
        Task secondDownload = Task.CompletedTask;
        try
        {
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            fixture.CopyLocally("local before second command");
            secondDownload = fixture.DownloadAsync(token);
            await secondDownload;

            Assert.IsTrue(firstRequestToken.IsCancellationRequested,
                "The remote request must participate in the singleton download's cancellation.");
            Assert.AreEqual("remote latest", fixture.LocalText);
            Assert.AreEqual(1, fixture.ClipboardWrites);
        }
        finally
        {
            resume.TrySetResult();
            await Task.WhenAll(firstDownload, secondDownload);
        }
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task QuickDownload_WhenSuperseded_ShouldOnlyPasteForTheWinningCommand(bool firstPaste, bool secondPaste)
    {
        var token = TestContext.CancellationTokenSource.Token;
        await using var fixture = await Fixture.CreateAsync(token);
        var firstRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        fixture.Adapter.Setup(adapter => adapter.GetProfileAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(async cancellationToken =>
            {
                var first = Interlocked.Increment(ref requests) == 1;
                (first ? firstRequested : secondRequested).TrySetResult();
                await resume.Task.WaitAsync(cancellationToken);
                return await new TextProfile(first ? "remote superseded" : "remote latest").ToProfileDto(cancellationToken);
            });

        var firstDownload = fixture.DownloadAsync(token, firstPaste);
        Task secondDownload = Task.CompletedTask;
        try
        {
            await firstRequested.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            secondDownload = fixture.DownloadAsync(token, secondPaste);
            await secondRequested.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            await firstDownload;

            Assert.IsEmpty(fixture.PastedTexts, "A canceled command must not paste while the winning request is pending.");
            resume.TrySetResult();
            await secondDownload;

            string[] expected = secondPaste ? ["remote latest"] : [];
            CollectionAssert.AreEqual(expected, fixture.PastedTexts);
            Assert.AreEqual("remote latest", fixture.LocalText);
            Assert.AreEqual(1, fixture.ClipboardWrites);
        }
        finally
        {
            resume.TrySetResult();
            await Task.WhenAll(firstDownload, secondDownload);
        }
    }

    private sealed class MemoryHistoryDbContext : HistoryDbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite("Data Source=:memory:");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private const string DownloadCommandId = "95396FFF-E5FE-45D3-9D70-4A43FA34FF31";
        private const string DownloadAndPasteCommandId = "8a4a033e-31da-1b87-76ea-548885866b66";
        private readonly ConfigurationTestServices _configurationServices = new();
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"QuickDownloadTests-{Guid.NewGuid():N}");
        private readonly MemoryHistoryDbContext _db = new();
        private readonly SemaphoreSlim _historySemaphore = new(1, 1);
        private readonly ServiceProvider _services;
        private readonly RemoteClipboardServerFactory _remoteFactory;
        private readonly HotkeyManager _hotkeys;
        private readonly DownloadService _download;
        private readonly Mock<IClipboardMoniter> _monitor = new();
        private readonly Mock<IClipboardChangingListener> _listener = new();
        private readonly Mock<ILogger> _logger = new();

        public Mock<IStorageBasedServerAdapter> Adapter { get; } = new();
        public string RemoteText { get; set; } = "remote first";
        public string LocalText { get; private set; } = "local initial";
        public int ClipboardWrites { get; private set; }
        public Action? BeforeClipboardWrite { get; set; }
        public List<string> PastedTexts { get; } = [];
        public Exception? ClipboardWriteException { get; set; }

        private Fixture()
        {
            Directory.CreateDirectory(_directory);
            var configPath = Path.Combine(_directory, "SyncClipboard.json");
            File.WriteAllText(configPath, """{"ConfigVersion":1}""");
            var config = new ConfigManager(configPath, _configurationServices.Upgrader);
            config.SetConfig(new SyncConfig { SyncSwitchOn = false, NotifyOnDownloaded = false });
            config.SetConfig(new HistoryConfig { EnableHistory = false });

            var clipboard = new Mock<IClipboardFactory>();
            clipboard.Setup(factory => factory.CreateProfileFromLocal(It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult<Profile>(new TextProfile(LocalText)));
            var setter = new Mock<IClipboardSetter<TextProfile>>();
            setter.Setup(s => s.SetLocalClipboard(It.IsAny<ClipboardMetaInfomation>(), It.IsAny<CancellationToken>()))
                .Callback<ClipboardMetaInfomation, CancellationToken>((meta, _) =>
                {
                    if (ClipboardWriteException is { } exception)
                    {
                        throw exception;
                    }
                    LocalText = meta.Text!;
                    ClipboardWrites++;
                }).Returns(Task.CompletedTask);
            var dispatcher = new Mock<IThreadDispatcher>();
            dispatcher.Setup(d => d.RunOnMainThreadAsync(It.IsAny<Func<Task>>()))
                .Returns<Func<Task>>(action => action());
            var profileEnv = new Mock<IProfileEnv>();
            profileEnv.Setup(env => env.GetPersistentDir()).Returns(_directory);
            var permissions = new Mock<IInputPermissionProvider>();
            permissions.Setup(p => p.GetSimulationStatus()).Returns(new InputPermissionStatus(
                InputPermissionState.NotRequired, InputPermissionState.NotRequired, InputPermissionState.Available));
            var simulator = new Mock<IEventSimulator>();
            simulator.Setup(s => s.SimulateKeyPress(KeyCode.VcV)).Callback(() => PastedTexts.Add(LocalText));
            Adapter.Setup(adapter => adapter.GetProfileAsync(It.IsAny<CancellationToken>()))
                .Returns<CancellationToken>(async token => await new TextProfile(RemoteText).ToProfileDto(token));

            // As in HistoryQueryCompatibilityTests, isolate history queries from the user's on-disk database.
            var history = (HistoryManager)RuntimeHelpers.GetUninitializedObject(typeof(HistoryManager));
            typeof(HistoryManager).GetField("_dbContext", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(history, _db);
            typeof(HistoryManager).GetField("_dbSemaphore", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(history, _historySemaphore);

            _services = new ServiceCollection()
                .AddSingleton(config)
                .AddSingleton<AccountManager>()
                .AddSingleton(_logger.Object)
                .AddSingleton(Mock.Of<ITrayIcon>())
                .AddSingleton(Mock.Of<INotificationManager>())
                .AddSingleton(profileEnv.Object)
                .AddSingleton(clipboard.Object)
                .AddSingleton(setter.Object)
                .AddSingleton(dispatcher.Object)
                .AddSingleton(history)
                .AddSingleton<LocalClipboardSetter>()
                .AddSingleton(_ => new VirtualKeyboard(permissions.Object, () => simulator.Object))
                .AddKeyedSingleton<ServerAdapterFactory>("test", (_, _) => _ => Adapter.Object)
                .BuildServiceProvider();
            _remoteFactory = new RemoteClipboardServerFactory(_services);
            _remoteFactory.ResetCurrentServer(
                new AccountConfig { AccountType = "test", AccountId = "test-account" }, new object());
            _hotkeys = new HotkeyManager(Mock.Of<INativeHotkeyRegistry>(), config);

            // Only its command descriptors are needed; avoid UploadService's global AppCore startup dependency.
            var upload = (UploadService)RuntimeHelpers.GetUninitializedObject(typeof(UploadService));
            var messenger = new StrongReferenceMessenger();
            messenger.Register<EmptyMessage, string>(this, SyncService.PULL_START_ENENT_NAME,
                (_, _) => BeforeClipboardWrite?.Invoke());
            _download = new DownloadService(_services, messenger, upload,
                _services.GetRequiredService<VirtualKeyboard>(), _monitor.Object, _listener.Object,
                _hotkeys, _remoteFactory,
                new ProfileNotificationHelper(Mock.Of<INotification>(),
                    new ProfileActionBuilder(_services.GetRequiredService<LocalClipboardSetter>(), profileEnv.Object)),
                _services.GetRequiredService<LocalClipboardSetter>());
        }

        public static async Task<Fixture> CreateAsync(CancellationToken token)
        {
            var fixture = new Fixture();
            await fixture._db.Database.OpenConnectionAsync(token);
            await fixture._db.Database.EnsureCreatedAsync(token);
            fixture._download.Start();
            return fixture;
        }

        public void CopyLocally(string text)
        {
            LocalText = text;
            _monitor.Raise(monitor => monitor.ClipboardChanged += null);
            _listener.Raise(listener => listener.Changed += null,
                new ClipboardMetaInfomation { Text = text }, new TextProfile(text));
        }

        public async Task DownloadAsync(CancellationToken token, bool paste = false)
        {
            var commandId = paste ? DownloadAndPasteCommandId : DownloadCommandId;
            Assert.IsTrue(_hotkeys.HotkeyStatusMap.ContainsKey(commandId));
            var context = new CommandCompletionContext();
            var previousContext = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(context);
                _hotkeys.RunCommand(commandId);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
            await context.Completion.WaitAsync(TimeSpan.FromSeconds(10), token);
            _logger.Verify(logger => logger.WriteAsync(It.IsAny<string>(),
                It.Is<string>(message => message.Contains("failed", StringComparison.OrdinalIgnoreCase))), Times.Never);
        }

        public void VerifySkippedWrite()
        {
            _logger.Verify(logger => logger.WriteAsync("PULL",
                "Skipped setting remote profile because the local clipboard changed"), Times.Once);
        }

        public async ValueTask DisposeAsync()
        {
            await _download.StopAsync();
            _remoteFactory.Dispose();
            await _services.DisposeAsync();
            await _db.DisposeAsync();
            _historySemaphore.Dispose();
            _configurationServices.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }

    // QuickDownload is async void. Wait for its actual completion, including finally, instead of sleeping
    // or treating PULL_STOP (which is sent before command completion) as an awaitable command boundary.
    private sealed class CommandCompletionContext : SynchronizationContext
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _operations;
        public Task Completion => _completion.Task;

        public override void OperationStarted() => Interlocked.Increment(ref _operations);

        public override void OperationCompleted()
        {
            if (Interlocked.Decrement(ref _operations) == 0)
            {
                _completion.TrySetResult();
            }
        }

        public override void Post(SendOrPostCallback callback, object? state)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var previousContext = Current;
                try
                {
                    SetSynchronizationContext(this);
                    callback(state);
                }
                catch (Exception ex)
                {
                    _completion.TrySetException(ex);
                }
                finally
                {
                    SetSynchronizationContext(previousContext);
                }
            });
        }
    }
}
