using System.Drawing;
namespace SyncClipboard.Core.Clipboard;

public class MetaInfomation
{
    public string? Text;
    public string? Html;
    public Image? Image;
    public string[]? Files;
    public DragDropEffects? Effects;
}
