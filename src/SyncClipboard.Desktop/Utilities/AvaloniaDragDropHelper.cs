using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using SyncClipboard.Desktop.ClipboardAva;
using System;
using System.Threading.Tasks;
using AvaloniaDragDropEffects = Avalonia.Input.DragDropEffects;

namespace SyncClipboard.Desktop.Utilities;

internal static class AvaloniaDragDropHelper
{
    // 从 UI 线程调用；填充失败或来源失效时清理数据包，拖拽失败时静默结束。
    public static async Task DoDragDropAsync(
        PointerPressedEventArgs triggerEvent, Func<DataTransfer, Task<bool>> fillPackage)
    {
        var dataTransfer = new AutoDisposeDataTransfer(new DataTransfer());
        try
        {
            if (!await fillPackage(dataTransfer.Data))
            {
                dataTransfer.Dispose();
                return;
            }

            // 异步填充期间来源可能已脱离窗口或窗口已关闭，此时数据包仍由我们负责释放。
            if (TopLevel.GetTopLevel(triggerEvent.Source as Visual)?.PlatformImpl is null)
            {
                dataTransfer.Dispose();
                return;
            }
        }
        catch
        {
            dataTransfer.Dispose();
            return;
        }

        // 来源有效后将数据包交给 Avalonia；提交后的异常不能作为自行释放的依据。
        try
        {
            await DragDrop.DoDragDropAsync(triggerEvent, dataTransfer, AvaloniaDragDropEffects.Copy);
        }
        catch { }
    }
}
