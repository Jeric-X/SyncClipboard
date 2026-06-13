using SyncClipboard.Core.Models;

namespace SyncClipboard.Core.Utilities;

/// <summary>
/// 窗口位置计算辅助类
/// </summary>
public static class WindowPositionHelper
{
    /// <summary>
    /// 计算窗口在光标/插入符附近的位置
    /// 窗口左上角在光标右下方，使用光标尺寸作为偏移
    /// </summary>
    public static (int x, int y) CalculateNearCaretPosition(
        ScreenPosition caretPosition,
        int windowWidth, int windowHeight,
        int workAreaX, int workAreaY, int workAreaWidth, int workAreaHeight)
    {
        // 水平位置：窗口左侧在光标右侧，偏移量为光标宽度
        var posX = caretPosition.X + caretPosition.Width;

        // 垂直位置：窗口顶部在光标底部，偏移量为光标高度
        var posY = caretPosition.Y + caretPosition.Height;

        // 如果垂直方向超出屏幕底部，则翻转到光标上方
        if (posY + windowHeight > workAreaY + workAreaHeight)
        {
            posY = caretPosition.Y - windowHeight;
        }

        // 确保窗口在工作区域内（不翻转水平位置，只做边界约束）
        posX = Math.Max(workAreaX, Math.Min(posX, workAreaX + workAreaWidth - windowWidth));
        posY = Math.Max(workAreaY, Math.Min(posY, workAreaY + workAreaHeight - windowHeight));

        return (posX, posY);
    }

    /// <summary>
    /// 计算窗口在鼠标位置的位置
    /// 窗口中心点在鼠标位置
    /// </summary>
    public static (int x, int y) CalculateNearMousePosition(
        ScreenPosition mousePosition,
        int windowWidth, int windowHeight,
        int workAreaX, int workAreaY, int workAreaWidth, int workAreaHeight)
    {
        // 窗口中心点在鼠标位置
        var posX = mousePosition.X - (windowWidth / 2);
        var posY = mousePosition.Y - (windowHeight / 2);

        // 确保窗口在工作区域内
        posX = Math.Max(workAreaX, Math.Min(posX, workAreaX + workAreaWidth - windowWidth));
        posY = Math.Max(workAreaY, Math.Min(posY, workAreaY + workAreaHeight - windowHeight));

        return (posX, posY);
    }

    /// <summary>
    /// 计算窗口在屏幕中央的位置
    /// </summary>
    public static (int x, int y) CalculateCenterOnScreenPosition(
        int windowWidth, int windowHeight,
        int workAreaX, int workAreaY, int workAreaWidth, int workAreaHeight)
    {
        var x = workAreaX + ((workAreaWidth - windowWidth) / 2);
        var y = workAreaY + ((workAreaHeight - windowHeight) / 2);

        return (x, y);
    }
}
