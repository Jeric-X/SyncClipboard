using Microsoft.Extensions.DependencyInjection;
using Moq;
using SharpHook;
using SharpHook.Data;
using SharpHook.Providers;
using SharpHook.Simulation;
using SyncClipboard.Core;
using SyncClipboard.Core.Models.Keyboard;
using SyncClipboard.Core.Utilities.Keyboard;
using System.Text.Json;

namespace SyncClipboard.Test;

[TestClass]
[TestCategory("NonUI")]
public class SharpHookKeyboardTests
{
    [TestMethod]
    [DataRow(KeyCode.VcLeftControl, Key.Ctrl)]
    [DataRow(KeyCode.VcRightControl, Key.Ctrl)]
    [DataRow(KeyCode.VcLeftShift, Key.Shift)]
    [DataRow(KeyCode.VcRightShift, Key.Shift)]
    [DataRow(KeyCode.VcLeftAlt, Key.Alt)]
    [DataRow(KeyCode.VcRightAlt, Key.Alt)]
    [DataRow(KeyCode.VcLeftMeta, Key.Meta)]
    [DataRow(KeyCode.VcRightMeta, Key.Meta)]
    [DataRow(KeyCode.VcSection, Key.OEM_102)]
    [DataRow(KeyCode.VcC, Key.C)]
    [DataRow(KeyCode.VcV, Key.V)]
    [DataRow(KeyCode.VcHanja, Key.Hanja)]
    [DataRow(KeyCode.VcKana, Key.Kana)]
    public void InputMapping_PreservesLogicalKeys(KeyCode nativeKey, Key key)
    {
        Assert.AreEqual(key, KeyCodeMap.Map[nativeKey]);
    }

    [TestMethod]
    [DataRow(Key.Ctrl, KeyCode.VcLeftControl)]
    [DataRow(Key.Shift, KeyCode.VcLeftShift)]
    [DataRow(Key.Alt, KeyCode.VcLeftAlt)]
    [DataRow(Key.Meta, KeyCode.VcLeftMeta)]
    public void ModifierSimulation_PreservesLeftHandChoice(Key key, KeyCode nativeKey)
    {
        Assert.AreEqual(nativeKey, KeyCodeMap.MapReverse[key]);
    }

    [TestMethod]
    [DataRow(Key.Kanji, Key.Hanja, KeyCode.VcHanja, "{\"Keys\":[\"Ctrl\",\"Kanji\"]}")]
    [DataRow(Key.Hangul, Key.Kana, KeyCode.VcKana, "{\"Keys\":[\"Ctrl\",\"Hangul\"]}")]
    public void LegacyAliases_PreserveStoredNamesAndMatchCanonicalInput(
        Key legacy, Key canonical, KeyCode nativeKey, string json)
    {
        var original = new Hotkey(Key.Ctrl, legacy);
        var restored = JsonSerializer.Deserialize<Hotkey>(json)!;

        Assert.AreEqual(original, restored);
        CollectionAssert.Contains(restored.Keys, legacy);
        Assert.AreEqual(nativeKey, KeyCodeMap.MapReverse[legacy]);
        var normalized = KeyCodeMap.NormalizeHotkey(restored);
        Assert.AreEqual(new Hotkey(Key.Ctrl, canonical), normalized);
        Assert.AreEqual(new Hotkey(Key.Ctrl, KeyCodeMap.Map[nativeKey]), normalized);
        Assert.AreEqual(json, JsonSerializer.Serialize(restored));
        Assert.AreEqual(normalized.GetHashCode(), new Hotkey(Key.Ctrl, canonical).GetHashCode());
    }

    [TestMethod]
    public void ReverseMapping_RoundtripsAllSupportedLogicalKeys()
    {
        foreach (var (key, nativeKey) in KeyCodeMap.MapReverse)
        {
            Assert.AreEqual(KeyCodeMap.NormalizeHotkey(new Hotkey(key)), new Hotkey(KeyCodeMap.Map[nativeKey]),
                $"Reverse mapping lost {key}");
        }

        var unmapped = new Hotkey(Key.GAMEPAD_A, Key.Ctrl);
        Assert.AreEqual(unmapped, KeyCodeMap.NormalizeHotkey(unmapped));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Factory_ConfiguresBackendBeforeSimulationAndPreservesPasteOrder(bool isLinux)
    {
        var backend = new Mock<ILinuxBackendProvider>(MockBehavior.Strict);
        var simulation = new Mock<IEventSimulationProvider>(MockBehavior.Strict);
        var hookProvider = new Mock<IGlobalHookProvider>(MockBehavior.Strict);
        var sequence = new MockSequence();
        if (isLinux)
            backend.InSequence(sequence).Setup(x => x.SetLinuxMode(LinuxMode.XRecord)).Returns(UioHookResult.Success);
        simulation.InSequence(sequence).Setup(x => x.InitializeVirtualDevices("SyncClipboard")).Returns(UioHookResult.Success);
        List<(EventType Type, KeyCode Key)> events = [];
        simulation.Setup(x => x.PostEvent(ref It.Ref<UioHookEvent>.IsAny))
            .Callback((ref UioHookEvent input) => events.Add((input.Type, input.Keyboard.KeyCode)))
            .Returns(UioHookResult.Success);
        simulation.Setup(x => x.DestroyVirtualDevices()).Returns(UioHookResult.Success);

        var factory = new SharpHookFactory(isLinux, backend.Object, simulation.Object, hookProvider.Object);
        using (var hook = factory.CreateGlobalHook())
        {
            Assert.IsFalse(hook.IsRunning);
        }
        using (var simulator = factory.CreateEventSimulator())
        {
            new VirtualKeyboard(simulator).Paste();
        }

        var modifier = OperatingSystem.IsMacOS() ? KeyCode.VcLeftMeta : KeyCode.VcLeftControl;
        CollectionAssert.AreEqual(new[]
        {
            (EventType.KeyPressed, modifier), (EventType.KeyPressed, KeyCode.VcV),
            (EventType.KeyReleased, KeyCode.VcV), (EventType.KeyReleased, modifier)
        }, events.ToArray());
        backend.Verify(x => x.SetLinuxMode(LinuxMode.XRecord), isLinux ? Times.Once() : Times.Never());
        simulation.Verify(x => x.InitializeVirtualDevices("SyncClipboard"), Times.Once());
        simulation.Verify(x => x.DestroyVirtualDevices(), Times.Once());
        hookProvider.VerifyNoOtherCalls();
    }

    [TestMethod]
    public void Factory_BackendFailureDoesNotInitializeSimulation()
    {
        var backend = new Mock<ILinuxBackendProvider>(MockBehavior.Strict);
        var simulation = new Mock<IEventSimulationProvider>(MockBehavior.Strict);
        var hook = new Mock<IGlobalHookProvider>(MockBehavior.Strict);
        backend.Setup(x => x.SetLinuxMode(LinuxMode.XRecord)).Returns(UioHookResult.ErrorLinuxLoadBackend);

        Assert.ThrowsExactly<HookException>(() => new SharpHookFactory(true, backend.Object, simulation.Object, hook.Object));
        simulation.VerifyNoOtherCalls();
        hook.VerifyNoOtherCalls();
    }

    [TestMethod]
    public void Factory_SimulatorInitializationFailureIsReported()
    {
        var simulation = new Mock<IEventSimulationProvider>(MockBehavior.Strict);
        simulation.Setup(x => x.InitializeVirtualDevices("SyncClipboard")).Returns(UioHookResult.Failure);
        var factory = new SharpHookFactory(false, Mock.Of<ILinuxBackendProvider>(), simulation.Object,
            Mock.Of<IGlobalHookProvider>());

        Assert.ThrowsExactly<HookException>(() => factory.CreateEventSimulator());
        simulation.Verify(x => x.DestroyVirtualDevices(), Times.Never());
    }

    [TestMethod]
    public void RegisteredSimulator_IsSharedAndDisposedWithServices()
    {
        var simulation = new Mock<IEventSimulationProvider>(MockBehavior.Strict);
        simulation.Setup(x => x.InitializeVirtualDevices("SyncClipboard")).Returns(UioHookResult.Success);
        simulation.Setup(x => x.DestroyVirtualDevices()).Returns(UioHookResult.Success);
        var factory = new SharpHookFactory(false, Mock.Of<ILinuxBackendProvider>(), simulation.Object,
            Mock.Of<IGlobalHookProvider>());
        var collection = new ServiceCollection();
        AppCore.ConfigCommonService(collection);
        collection.AddSingleton(factory);
        IEventSimulator simulator;
        using (var services = collection.BuildServiceProvider())
        {
            simulator = services.GetRequiredService<IEventSimulator>();
            Assert.AreSame(simulator, services.GetRequiredService<IEventSimulator>());
            Assert.IsFalse(simulator.IsDisposed);
        }

        Assert.IsTrue(simulator.IsDisposed);
        simulation.Verify(x => x.InitializeVirtualDevices("SyncClipboard"), Times.Once());
        simulation.Verify(x => x.DestroyVirtualDevices(), Times.Once());
    }
}
