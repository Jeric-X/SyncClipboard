extern alias WinUIProduct;

using SyncClipboard.Core.Models.Keyboard;
using System.Runtime.InteropServices;
using WinUIProduct::SyncClipboard.WinUI3.Win32;
using static Vanara.PInvoke.ComCtl32;
using static Vanara.PInvoke.DbgHelp;
using static Vanara.PInvoke.User32;

namespace SyncClipboard.Test.WinUI3;

[TestClass]
[TestCategory("NonUI")]
public class VanaraInteropTests
{
    [TestMethod]
    [DataRow(0xA0, Key.Shift)]
    [DataRow(0xA1, Key.Shift)]
    [DataRow(0xA2, Key.Ctrl)]
    [DataRow(0xA3, Key.Ctrl)]
    [DataRow(0xA4, Key.Alt)]
    [DataRow(0xA5, Key.Alt)]
    [DataRow(0x5B, Key.Meta)]
    [DataRow(0x5C, Key.Meta)]
    [DataRow(0x15, Key.Hangul)]
    [DataRow(0x19, Key.Kanji)]
    public void WindowsKeyAliases_PreserveCoreKey(int virtualKey, Key expected)
    {
        Assert.AreEqual(expected, KeyboardMap.ConvertFromVirtualKey((Windows.System.VirtualKey)virtualKey));
    }

    [TestMethod]
    public void KeyboardMap_PreservesLetterRangeAndUnknownKeys()
    {
        for (var offset = 0; offset < 26; offset++)
        {
            var key = Enum.Parse<Key>(((char)('A' + offset)).ToString());
            var virtualKey = (Windows.System.VirtualKey)(0x41 + offset);
            Assert.AreEqual(key, KeyboardMap.ConvertFromVirtualKey(virtualKey));
            Assert.AreEqual(virtualKey, KeyboardMap.ConvertToVirtualKey(key));
        }
        Assert.IsNull(KeyboardMap.ConvertFromVirtualKey((Windows.System.VirtualKey)0xFF));
        Assert.IsNull(KeyboardMap.ConvertToVirtualKey((Key)int.MaxValue));
    }

    [TestMethod]
    public void TaskDialogLayout_UsesPackedWindowsAbi()
    {
        Assert.AreEqual(4 + nint.Size, Marshal.SizeOf<TASKDIALOG_BUTTON>());
        Assert.AreEqual((nint)4, Marshal.OffsetOf<TASKDIALOG_BUTTON>(nameof(TASKDIALOG_BUTTON.pszButtonText)));
        Assert.AreEqual(nint.Size == 8 ? 160 : 96, Marshal.SizeOf<TASKDIALOGCONFIG>());
        Assert.AreEqual((nint)(16 + (6 * nint.Size)),
            Marshal.OffsetOf<TASKDIALOGCONFIG>(nameof(TASKDIALOGCONFIG.pButtons)));
    }

    [TestMethod]
    public void MinidumpLayout_PreservesPackedExceptionPointerAndWin32Bool()
    {
        Assert.AreEqual(8 + nint.Size, Marshal.SizeOf<MINIDUMP_EXCEPTION_INFORMATION>());
        Assert.AreEqual((nint)4,
            Marshal.OffsetOf<MINIDUMP_EXCEPTION_INFORMATION>(nameof(MINIDUMP_EXCEPTION_INFORMATION.ExceptionPointers)));
        Assert.AreEqual((nint)(4 + nint.Size),
            Marshal.OffsetOf<MINIDUMP_EXCEPTION_INFORMATION>(nameof(MINIDUMP_EXCEPTION_INFORMATION.ClientPointers)));
    }

    [TestMethod]
    public void KeyboardHookLayout_DecodesSyntheticNativeBuffer()
    {
        Assert.AreEqual(16 + nint.Size, Marshal.SizeOf<KBDLLHOOKSTRUCT>());
        Assert.AreEqual((nint)16, Marshal.OffsetOf<KBDLLHOOKSTRUCT>(nameof(KBDLLHOOKSTRUCT.dwExtraInfo)));
        var memory = Marshal.AllocHGlobal(16 + nint.Size);
        try
        {
            Marshal.WriteInt32(memory, 0, 0x41);
            Marshal.WriteInt32(memory, 4, 0x1E);
            Marshal.WriteInt32(memory, 8, 0);
            Marshal.WriteInt32(memory, 12, 1234);
            Marshal.WriteIntPtr(memory, 16, (nint)5678);

            var decoded = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(memory);

            Assert.AreEqual(VK.VK_A, decoded.vkCode);
            Assert.AreEqual(0x1Eu, decoded.scanCode);
            Assert.AreEqual(1234u, decoded.time);
            Assert.AreEqual((nint)5678, decoded.dwExtraInfo);
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }
}
