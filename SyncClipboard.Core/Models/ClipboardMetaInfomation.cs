using SyncClipboard.Abstract;

namespace SyncClipboard.Core.Models;

public record class ClipboardMetaInfomation
{
    public string? Text;
    public string? Html;
    public IClipboardImage? Image;
    public string[]? Files;
    public DragDropEffects? Effects;
}
