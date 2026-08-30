using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;
using SyncClipboard.Core.UserServices;
using static SyncClipboard.Test.ForegroundWindowMonitorTests;

namespace SyncClipboard.Test;

[TestClass]
public class ForegroundWindowTrackingServiceTests
{
    [TestMethod]
    public void MacWindowNumber_IsPreferredOverTitleAndCoordinateSystems()
    {
        var history = new MacNativeWindowInfo
        {
            ProcessId = 209,
            BundleIdentifier = "syncclipboard",
            WindowNumber = 42,
            WindowTitle = "Clipboard History",
            Bounds = new ScreenPosition { X = 10, Y = 20, Width = 500, Height = 600 }
        };
        var sameNativeWindow = history with
        {
            WindowTitle = "剪贴板历史",
            Bounds = new ScreenPosition { X = 100, Y = 200, Width = 1000, Height = 1200 }
        };
        var otherNativeWindow = history with { WindowNumber = 43 };

        Assert.IsTrue(history.IsSameWindow(sameNativeWindow));
        Assert.IsFalse(history.IsSameWindow(otherNativeWindow));
    }

    [TestMethod]
    public void MacWindowIdentity_NeverDependsOnBounds()
    {
        var original = new MacNativeWindowInfo
        {
            ProcessId = 213,
            BundleIdentifier = "com.example.editor",
            WindowTitle = "Document",
            Bounds = new ScreenPosition { X = 10, Y = 20, Width = 500, Height = 600 }
        };
        var movedAndResized = original with
        {
            Bounds = new ScreenPosition { X = -1000, Y = 800, Width = 1600, Height = 300 }
        };
        var differentTitleAtSameBounds = original with { WindowTitle = "Other Document" };
        var boundsOnly = original with { WindowTitle = string.Empty };
        var movedBoundsOnly = movedAndResized with { WindowTitle = string.Empty };

        Assert.IsTrue(original.IsSameWindow(movedAndResized));
        Assert.IsFalse(original.IsSameWindow(differentTitleAtSameBounds));
        Assert.IsTrue(boundsOnly.IsSameWindow(movedBoundsOnly));
    }

    [TestMethod]
    public void Tracking_ExcludesOnlyRegisteredHistoryWindow()
    {
        var historyWindow = CreateNativeWindow(200, (nint)2001);
        var sameProcessOtherWindow = CreateNativeWindow(200, (nint)2002);
        var monitor = new FakeMonitor { Current = CreateDetail(historyWindow, "history") };
        var provider = new FakeProvider();
        var service = CreateService(monitor, provider);

        service.SetHistoryWindow(historyWindow);
        service.Start();
        Assert.IsNull(service.LastExternalWindow);

        monitor.Raise(CreateDetail(sameProcessOtherWindow, "settings"));

        Assert.IsNotNull(service.LastExternalWindow);
        Assert.IsTrue(sameProcessOtherWindow.IsSameWindow(service.LastExternalWindow.Value.NativeWindowInfo!));
        service.Stop();
    }

    [TestMethod]
    public void RegisteringHistoryWindow_RestoresTargetOverwrittenBeforeRegistration()
    {
        var externalWindow = CreateNativeWindow(207, (nint)2071);
        var historyWindow = CreateNativeWindow(208, (nint)2081);
        var monitor = new FakeMonitor { Current = CreateDetail(externalWindow, "editor") };
        var service = CreateService(monitor, new FakeProvider());
        service.Start();

        monitor.Raise(CreateDetail(historyWindow, "history"));
        service.SetHistoryWindow(historyWindow);

        Assert.IsNotNull(service.LastExternalWindow);
        Assert.IsTrue(externalWindow.IsSameWindow(service.LastExternalWindow.Value.NativeWindowInfo!));
        service.Stop();
    }

    [TestMethod]
    public void Tracking_QueriesInitialSnapshot_AndActivatesLastWindow()
    {
        var externalWindow = CreateNativeWindow(201, (nint)2011);
        var monitor = new FakeMonitor { Current = CreateDetail(externalWindow, "editor") };
        var provider = new FakeProvider();
        var service = CreateService(monitor, provider);

        service.Start();
        var result = service.TryActivateLastExternalWindow();

        Assert.IsTrue(result);
        Assert.IsNotNull(provider.ActivatedWindow);
        Assert.IsTrue(externalWindow.IsSameWindow(provider.ActivatedWindow));
        service.Stop();
    }

    [TestMethod]
    public void Tracking_IgnoresNullSnapshots()
    {
        var externalWindow = CreateNativeWindow(202, (nint)2021);
        var monitor = new FakeMonitor { Current = CreateDetail(externalWindow, "terminal") };
        var service = CreateService(monitor, new FakeProvider());
        service.Start();

        monitor.Raise(null);

        Assert.IsNotNull(service.LastExternalWindow);
        Assert.IsTrue(externalWindow.IsSameWindow(service.LastExternalWindow.Value.NativeWindowInfo!));
        service.Stop();
    }

    [TestMethod]
    public void Tracking_ConfirmsTheWindowUsedByTheLastActivationRequest()
    {
        var target = CreateNativeWindow(203, (nint)2031);
        var history = CreateNativeWindow(204, (nint)2041);
        var monitor = new FakeMonitor { Current = CreateDetail(target, "editor") };
        var provider = new FakeProvider { Current = CreateDetail(history, "history") };
        var service = CreateService(monitor, provider);
        service.SetHistoryWindow(history);
        service.Start();

        Assert.IsTrue(service.TryActivateLastExternalWindow());
        Assert.IsFalse(service.IsLastActivationTargetForeground());
        Assert.IsTrue(service.IsHistoryWindowForeground());

        provider.Current = CreateDetail(target, "editor");

        Assert.IsTrue(service.IsLastActivationTargetForeground());
        Assert.IsFalse(service.IsHistoryWindowForeground());
        service.Stop();
    }

    [TestMethod]
    public void HistoryForegroundCheck_DoesNotExcludeOtherWindowsFromSameProcess()
    {
        var history = CreateNativeWindow(205, (nint)2051);
        var settings = CreateNativeWindow(205, (nint)2052);
        var provider = new FakeProvider { Current = CreateDetail(settings, "settings") };
        var service = CreateService(new FakeMonitor(), provider);
        service.SetHistoryWindow(history);

        Assert.IsFalse(service.IsHistoryWindowForeground());
    }

    [TestMethod]
    public void HistoryForegroundCheck_TreatsUnidentifiableMacWindowFromOwnProcessAsUnsafe()
    {
        var history = new MacNativeWindowInfo
        {
            ProcessId = 206,
            BundleIdentifier = "syncclipboard",
            WindowTitle = "History",
            Bounds = new ScreenPosition { X = 10, Y = 10, Width = 500, Height = 600 }
        };
        var unidentifiableCurrentWindow = new MacNativeWindowInfo
        {
            ProcessId = 206,
            BundleIdentifier = "syncclipboard"
        };
        var provider = new FakeProvider
        {
            Current = CreateDetail(unidentifiableCurrentWindow, string.Empty)
        };
        var service = CreateService(new FakeMonitor(), provider);
        service.SetHistoryWindow(history);

        Assert.IsTrue(service.IsHistoryWindowForeground());
    }

    [TestMethod]
    public void Tracking_ExcludesRepositionedMacHistoryWindowWhenAxWindowNumberIsMissing()
    {
        var history = new MacNativeWindowInfo
        {
            ProcessId = 211,
            BundleIdentifier = "syncclipboard",
            WindowNumber = 10788,
            WindowTitle = "历史记录",
            Bounds = new ScreenPosition { X = 358, Y = 185, Width = 681, Height = 416 }
        };
        var repositionedHistoryFromAccessibility = new MacNativeWindowInfo
        {
            ProcessId = 211,
            BundleIdentifier = "syncclipboard",
            WindowTitle = "历史记录",
            Bounds = new ScreenPosition { X = -52, Y = 194, Width = 681, Height = 416 }
        };
        var external = CreateNativeWindow(212, (nint)2121);
        var monitor = new FakeMonitor { Current = CreateDetail(external, "editor") };
        var service = CreateService(monitor, new FakeProvider());
        service.SetHistoryWindow(history);
        service.Start();

        monitor.Raise(CreateDetail(repositionedHistoryFromAccessibility, "历史记录"));

        Assert.IsNotNull(service.LastExternalWindow);
        Assert.IsTrue(external.IsSameWindow(service.LastExternalWindow.Value.NativeWindowInfo!));
        service.Stop();
    }

    [TestMethod]
    public void ActivationConfirmation_FallsBackToFrontmostProcessForUnidentifiableMacTarget()
    {
        var target = new MacNativeWindowInfo
        {
            ProcessId = 210,
            BundleIdentifier = "com.openai.codex"
        };
        var monitor = new FakeMonitor { Current = CreateDetail(target, string.Empty) };
        var provider = new FakeProvider { Current = CreateDetail(target, string.Empty) };
        var service = CreateService(monitor, provider);
        service.Start();

        Assert.IsTrue(service.TryActivateLastExternalWindow());
        Assert.IsTrue(service.IsLastActivationTargetForeground());
        service.Stop();
    }

    [TestMethod]
    public void DisabledTracking_DoesNotSubscribeOrReadCurrentWindow()
    {
        var monitor = new FakeMonitor
        {
            Current = CreateDetail(CreateNativeWindow(214, (nint)2141), "editor")
        };
        var service = new ForegroundWindowTrackingService(
            monitor,
            new FakeProvider(),
            new FakeLogger(),
            trackingEnabled: false);

        service.Start();

        Assert.AreEqual(0, monitor.SubscriberCount);
        Assert.AreEqual(0, monitor.CurrentReadCount);
        Assert.IsNull(service.LastExternalWindow);
        service.Stop();
    }

    [TestMethod]
    public void CaptureCurrentForegroundWindow_RefreshesTargetImmediately()
    {
        var first = CreateNativeWindow(215, (nint)2151);
        var second = CreateNativeWindow(215, (nint)2152);
        var monitor = new FakeMonitor { Current = CreateDetail(first, "first") };
        var service = CreateService(monitor, new FakeProvider());
        service.Start();
        monitor.Current = CreateDetail(second, "second");

        service.CaptureCurrentForegroundWindow();

        Assert.IsNotNull(service.LastExternalWindow);
        Assert.IsTrue(second.IsSameWindow(service.LastExternalWindow.Value.NativeWindowInfo!));
        Assert.AreEqual(2, monitor.CurrentReadCount);
        service.Stop();
    }

    private sealed class FakeMonitor : IForegroundWindowMonitor
    {
        private Action<WindowDetail?>? _foregroundWindowChanged;

        public WindowDetail? Current { get; set; }
        public int CurrentReadCount { get; private set; }
        public int SubscriberCount => _foregroundWindowChanged?.GetInvocationList().Length ?? 0;

        public event Action<WindowDetail?>? ForegroundWindowChanged
        {
            add => _foregroundWindowChanged += value;
            remove => _foregroundWindowChanged -= value;
        }

        public WindowDetail? GetCurrentForegroundWindow()
        {
            CurrentReadCount++;
            return Current;
        }

        public void Raise(WindowDetail? detail) => _foregroundWindowChanged?.Invoke(detail);
    }

    private static ForegroundWindowTrackingService CreateService(
        IForegroundWindowMonitor monitor,
        INativeWindowController provider) =>
        new(monitor, provider, new FakeLogger(), trackingEnabled: true);
}
