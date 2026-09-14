extern alias WinUIProduct;

using System.Runtime.InteropServices;
using Vanara.PInvoke;
using WinUIProduct::SyncClipboard.WinUI3.Services;
using static Vanara.PInvoke.ComCtl32;

namespace SyncClipboard.Test.WinUI3;

[TestClass]
[TestCategory("NonUI")]
public class WinUIGlobalDialogTests
{
    [TestMethod]
    [DataRow(100, true)]
    [DataRow(101, false)]
    [DataRow(2, false)]
    public async Task Confirmation_PreservesNativePayloadAndMapsSelection(int selectedButtonId, bool expected)
    {
        TASKDIALOGCONFIG? captured = null;
        var callCount = 0;
        var dialog = new WinUIGlobalDialog(config =>
        {
            captured = config;
            callCount++;
            AssertConfig(config, 2);
            AssertButton(config, 0, 100, "继续 ✓");
            AssertButton(config, 1, 101, "取消");
            return (HRESULT.S_OK, selectedButtonId);
        });

        var confirmed = await dialog.ShowConfirmationAsync("同步确认", "文件\n第二行", "继续 ✓", "取消");

        Assert.AreEqual(expected, confirmed);
        Assert.AreEqual(1, callCount);
        AssertDisposed(captured);
    }

    [TestMethod]
    public async Task Message_UsesOneCloseButtonAndDisposesNativeStrings()
    {
        TASKDIALOGCONFIG? captured = null;
        var dialog = new WinUIGlobalDialog(config =>
        {
            captured = config;
            AssertConfig(config, 1);
            AssertButton(config, 0, 101, "关闭");
            return (HRESULT.S_OK, 101);
        });

        await dialog.ShowMessageAsync("同步确认", "文件\n第二行", "关闭");

        AssertDisposed(captured);
    }

    [TestMethod]
    public void FailedHResult_PropagatesErrorAndDisposesNativeStrings()
    {
        TASKDIALOGCONFIG? captured = null;
        var dialog = new WinUIGlobalDialog(config =>
        {
            captured = config;
            AssertButton(config, 0, 100, "继续");
            return (HRESULT.E_FAIL, 100);
        });

        var error = Assert.ThrowsExactly<COMException>(() =>
            dialog.ShowConfirmationAsync("同步确认", "文件\n第二行", "继续", "取消"));

        Assert.AreEqual(unchecked((int)0x80004005), error.HResult);
        AssertDisposed(captured);
    }

    [TestMethod]
    public void InvocationException_PropagatesOriginalErrorAndDisposesNativeStrings()
    {
        TASKDIALOGCONFIG? captured = null;
        var expected = new InvalidOperationException("native invocation failed");
        var dialog = new WinUIGlobalDialog(config =>
        {
            captured = config;
            AssertButton(config, 0, 101, "关闭");
            throw expected;
        });

        var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            dialog.ShowMessageAsync("同步确认", "文件\n第二行", "关闭"));

        Assert.AreSame(expected, error);
        AssertDisposed(captured);
    }

    private static void AssertConfig(TASKDIALOGCONFIG config, uint buttonCount)
    {
        Assert.AreEqual((uint)Marshal.SizeOf<TASKDIALOGCONFIG>(), config.cbSize);
        Assert.AreEqual("同步确认", config.WindowTitle);
        Assert.AreEqual("文件\n第二行", config.Content);
        Assert.AreEqual(buttonCount, config.cButtons);
        Assert.AreEqual(101, config.nDefaultButton);
        Assert.AreEqual(TASKDIALOG_FLAGS.TDF_ALLOW_DIALOG_CANCELLATION | TASKDIALOG_FLAGS.TDF_SIZE_TO_CONTENT,
            config.dwFlags);
        Assert.AreEqual((nint)TaskDialogIcon.TD_ERROR_ICON, config.mainIcon);
        Assert.AreEqual(0u, config.cRadioButtons);
    }

    private static void AssertButton(TASKDIALOGCONFIG config, int index, int id, string text)
    {
        var pointer = config.pButtons + (index * Marshal.SizeOf<TASKDIALOG_BUTTON>());
        var button = Marshal.PtrToStructure<TASKDIALOG_BUTTON>(pointer);
        Assert.AreEqual(id, button.nButtonID);
        Assert.AreEqual(text, Marshal.PtrToStringUni(button.pszButtonText));
    }

    private static void AssertDisposed(TASKDIALOGCONFIG? config)
    {
        Assert.IsNotNull(config);
        // Inspect cleared fields only; never dereference the released button memory.
        Assert.AreEqual(nint.Zero, config.pszWindowTitle);
        Assert.AreEqual(nint.Zero, config.pszContent);
    }
}
