﻿using SyncClipboard.Abstract;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.Models;

public record class ClipboardMetaInfomation
{
    public const string ImageType = "Image";

    public int? TimeStamp;
    public bool? ExcludeForSync;
    public bool? ExcludeForHistory;
    public string? Text;
    public string? Html;
    public IClipboardImage? Image;
    public string[]? _files;
    public string[]? Files
    {
        get => (string[]?)_files?.Clone();
        set
        {
            _files = (string[]?)value?.Clone();
            if (_files != null)
            {
                Array.Sort(_files);
            }
        }
    }
    public DragDropEffects? Effects;
    public string? OriginalType;

    public virtual bool Equals(ClipboardMetaInfomation? other)
    {
        if (TimeStamp is not null && other?.TimeStamp is not null)
        {
            return TimeStamp == other.TimeStamp;
        }

        return (object)this == other ||
            (other is not null
            && EqualityContract == other.EqualityContract
            && EqualityComparer<bool?>.Default.Equals(ExcludeForSync, other.ExcludeForSync)
            && EqualityComparer<bool?>.Default.Equals(ExcludeForHistory, other.ExcludeForHistory)
            && EqualityComparer<string>.Default.Equals(Text, other.Text)
            && EqualityComparer<string>.Default.Equals(Html, other.Html)
            && EqualityComparer<IClipboardImage>.Default.Equals(Image, other.Image)
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
        if (TimeStamp is not null)
        {
            return TimeStamp.Value;
        }

        List<int> hashList =
        [
            EqualityContract.GetHashCode(),
            Text?.GetHashCode() ?? 0,
            Html?.GetHashCode() ?? 0,
            Files?.ListHashCode() ?? 0,
            Effects?.GetHashCode() ?? 0,
            OriginalType?.GetHashCode() ?? 0,
            Image?.GetHashCode() ?? 0,
        ];

        return hashList.ListHashCode();
    }

    public override string ToString()
    {
        return $"Text={Text} Html={Html} Files='{string.Join(',', Files ?? [])}' DragDropEffects={Effects} OriginalType={OriginalType}";
    }

    public bool Empty()
    {
        return Text is null && Html is null && Files is null && Image is null;
    }
}