using SyncClipboard.Abstract;

namespace SyncClipboard.Core.Models;

public record class ClipboardMetaInfomation
{
    public const string ImageType = "Image";

    public string? Text;
    public string? Html;
    public IClipboardImage? Image;
    public string[]? Files;
    public DragDropEffects? Effects;
    public string? OriginalType;

    public virtual bool Equals(ClipboardMetaInfomation? other)
    {
        return (object)this == other ||
            (other is not null
            && EqualityContract == other.EqualityContract
            && EqualityComparer<string>.Default.Equals(Text, other.Text)
            && EqualityComparer<string>.Default.Equals(Html, other.Html)
            //&& EqualityComparer<IClipboardImage>.Default.Equals(Image, other.Image)
            && SameFIles(other.Files)
            && EqualityComparer<DragDropEffects?>.Default.Equals(Effects, other.Effects)
            && EqualityComparer<string>.Default.Equals(OriginalType, other.OriginalType));
    }

    private bool SameFIles(string[]? other)
    {
        return Files == other ||
            (other is not null && Files.AsSpan().SequenceEqual(other));
    }

    public override int GetHashCode()
    {
        return ((((EqualityContract.GetHashCode() * -1521134295
            + Text?.GetHashCode() ?? 0) * -1521134295
            + Html?.GetHashCode() ?? 0) * -1521134295
            + GetFileHashCode()) * -1521134295
            + Effects?.GetHashCode() ?? 0) * -1521134295
            + OriginalType?.GetHashCode() ?? 0;
    }

    private int GetFileHashCode()
    {
        if (Files is null)
        {
            return 0;
        }

        int hash = 0;
        foreach (var file in Files)
        {
            hash = hash * -1521134295 + file.GetHashCode();
        }
        return hash;
    }
}
