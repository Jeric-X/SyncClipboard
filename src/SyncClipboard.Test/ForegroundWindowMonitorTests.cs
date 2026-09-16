using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Test;

[TestClass]
public class ForegroundWindowMonitorTests
{
    [TestMethod]
    public void Subscribers_ControlNativeWatcherLifetime()
    {
        var watcher = new FakeWatcher();
        using var monitor = CreateMonitor(watcher, new FakeProvider());

        static void First(WindowDetail? _) { }
        static void Second(WindowDetail? _) { }

        monitor.ForegroundWindowChanged += First;
        monitor.ForegroundWindowChanged += Second;

        Assert.AreEqual(1, watcher.StartCount);
        Assert.AreEqual(0, watcher.StopCount);

        monitor.ForegroundWindowChanged -= First;
        Assert.AreEqual(0, watcher.StopCount);

        monitor.ForegroundWindowChanged -= Second;
        Assert.AreEqual(1, watcher.StopCount);
    }

    [TestMethod]
    public void NewSubscriber_WaitsForChange_AndCanQueryCurrentSnapshot()
    {
        var expected = CreateDetail(101, (nint)1001, "current");
        var provider = new FakeProvider { Current = expected };
        var watcher = new FakeWatcher();
        using var monitor = CreateMonitor(watcher, provider);
        var callbackCount = 0;

        monitor.ForegroundWindowChanged += _ => callbackCount++;

        Assert.AreEqual(0, callbackCount);
        Assert.AreEqual(expected, monitor.GetCurrentForegroundWindow());
    }

    [TestMethod]
    public void NativeEvents_AreForwardedWithoutDeduplication_OnOriginalThread()
    {
        var native = CreateNativeWindow(102, (nint)1002);
        var provider = new FakeProvider { DetailForNativeWindow = CreateDetail(native, "changed") };
        var watcher = new FakeWatcher();
        using var monitor = CreateMonitor(watcher, provider);
        var callbackCount = 0;
        var callbackThreadId = -1;
        monitor.ForegroundWindowChanged += _ =>
        {
            callbackCount++;
            callbackThreadId = Environment.CurrentManagedThreadId;
        };

        var raisingThreadId = Task.Run(() =>
        {
            var threadId = Environment.CurrentManagedThreadId;
            watcher.Raise(native);
            watcher.Raise(native);
            return threadId;
        }).GetAwaiter().GetResult();

        Assert.AreEqual(2, callbackCount);
        Assert.AreEqual(2, provider.NativeReadCount);
        Assert.AreEqual(raisingThreadId, callbackThreadId);
    }

    [TestMethod]
    public void ReadAndSubscriberFailures_AreIsolated()
    {
        var watcher = new FakeWatcher();
        var provider = new FakeProvider { ThrowWhenReadingNativeWindow = true };
        var logger = new FakeLogger();
        using var monitor = new ForegroundWindowMonitor(watcher, provider, logger);
        WindowDetail? received = CreateDetail(1, (nint)1, "sentinel");
        monitor.ForegroundWindowChanged += _ => throw new InvalidOperationException("subscriber failed");
        monitor.ForegroundWindowChanged += detail => received = detail;

        watcher.Raise(CreateNativeWindow(103, (nint)1003));

        Assert.IsNull(received);
        Assert.IsTrue(logger.Messages.Any(message => message.Contains("Failed to read")));
        Assert.IsTrue(logger.Messages.Any(message => message.Contains("subscriber failed")));
    }

    private static ForegroundWindowMonitor CreateMonitor(FakeWatcher watcher, FakeProvider provider) =>
        new(watcher, provider, new FakeLogger());

    internal static WindowsNativeWindowInfo CreateNativeWindow(int processId, nint handle) => new()
    {
        ProcessId = processId,
        WindowHandle = handle
    };

    internal static WindowDetail CreateDetail(int processId, nint handle, string title) =>
        CreateDetail(CreateNativeWindow(processId, handle), title);

    internal static WindowDetail CreateDetail(NativeWindowInfo nativeWindow, string title) => new()
    {
        NativeWindowInfo = nativeWindow,
        WindowInfo = new WindowInfo
        {
            ProcessName = "test",
            ExecutableName = "test.exe",
            WindowTitle = title
        }
    };

    internal sealed class FakeWatcher : INativeForegroundWindowWatcher
    {
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public event Action<NativeWindowInfo?>? ForegroundWindowChanged;

        public void Start() => StartCount++;
        public void Stop() => StopCount++;
        public void Raise(NativeWindowInfo? window) => ForegroundWindowChanged?.Invoke(window);
        public void Dispose() { }
    }

    internal sealed class FakeProvider : INativeWindowController
    {
        public WindowDetail? Current { get; set; }
        public WindowDetail? DetailForNativeWindow { get; set; }
        public bool ThrowWhenReadingNativeWindow { get; set; }
        public int NativeReadCount { get; private set; }
        public NativeWindowInfo? ActivatedWindow { get; private set; }
        public bool ActivationResult { get; set; } = true;

        public WindowDetail? GetForegroundWindowDetail() => Current;
        public WindowInfo? GetForegroundWindowInfo() => Current?.WindowInfo;

        public WindowDetail? GetWindowDetail(NativeWindowInfo window)
        {
            NativeReadCount++;
            if (ThrowWhenReadingNativeWindow)
            {
                throw new InvalidOperationException("native read failed");
            }

            return DetailForNativeWindow;
        }

        public bool TryActivateWindow(NativeWindowInfo window)
        {
            ActivatedWindow = window;
            return ActivationResult;
        }
    }

    internal sealed class FakeLogger : ILogger
    {
        public List<string> Messages { get; } = [];
        public void Write(string? tag, string str) => Messages.Add(str);
        public void Write(string str) => Messages.Add(str);
        public Task WriteAsync(string? tag, string str)
        {
            Messages.Add(str);
            return Task.CompletedTask;
        }
        public Task WriteAsync(string str)
        {
            Messages.Add(str);
            return Task.CompletedTask;
        }
        public void Flush() { }
    }
}
