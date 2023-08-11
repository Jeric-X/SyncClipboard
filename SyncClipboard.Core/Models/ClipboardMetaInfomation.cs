using System.Drawing;

namespace SyncClipboard.Core.Models;

public class ClipboardMetaInfomation
{
    public string? Text;
    public string? Html;
    public Image? Image;
    public string[]? Files;
    public DragDropEffects? Effects;
}
