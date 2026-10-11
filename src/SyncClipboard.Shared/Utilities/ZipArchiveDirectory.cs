using System.Buffers.Binary;

namespace SyncClipboard.Shared.Utilities;

internal static class ZipArchiveDirectory
{
    private sealed record DirectoryRange(long Offset, long Length, long Count);

    public static async Task ValidateEntryCountAsync(Stream source, uint limit, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var directory = await ReadDirectoryRangeAsync(source, token).ConfigureAwait(false);
        CheckEntryCount(directory.Count, limit);
        var end = checked(directory.Offset + directory.Length);
        var header = new byte[46];
        source.Position = directory.Offset;
        long count = 0;
        while (source.Position < end)
        {
            token.ThrowIfCancellationRequested();
            CheckEntryCount(++count, limit);
            if (source.Position > end - header.Length)
                throw new InvalidDataException("Truncated ZIP central directory.");
            await source.ReadExactlyAsync(header, token).ConfigureAwait(false);
            if (U32(header, 0) != 0x02014b50)
                throw new InvalidDataException("Invalid ZIP directory entry.");
            var length = (long)U16(header, 28) + U16(header, 30) + U16(header, 32);
            if (length > end - source.Position)
                throw new InvalidDataException("Truncated ZIP directory entry.");
            source.Position += length;
        }
        if (count != directory.Count)
            throw new InvalidDataException("ZIP directory entry count mismatch.");
        source.Position = 0;
    }

    private static void CheckEntryCount(long count, uint limit)
    {
        if (count > limit)
            throw new InvalidDataException($"Group transfer data exceeds the file and directory limit ({limit}).");
    }

    private static async Task<DirectoryRange> ReadDirectoryRangeAsync(Stream source, CancellationToken token)
    {
        var tail = new byte[(int)Math.Min(source.Length, ushort.MaxValue + 22L)];
        source.Position = source.Length - tail.Length;
        await source.ReadExactlyAsync(tail, token).ConfigureAwait(false);
        for (var offset = tail.Length - 22; offset >= 0; offset--)
        {
            token.ThrowIfCancellationRequested();
            if (U32(tail, offset) != 0x06054b50 || offset + 22 + U16(tail, offset + 20) != tail.Length)
                continue;
            var eocdOffset = source.Length - tail.Length + offset;
            if (U16(tail, offset + 4) != 0 || U16(tail, offset + 6) != 0)
                throw new InvalidDataException("Split ZIP archives are unsupported.");
            if (U16(tail, offset + 10) == ushort.MaxValue || U32(tail, offset + 12) == uint.MaxValue ||
                U32(tail, offset + 16) == uint.MaxValue)
                return await ReadZip64DirectoryAsync(source, eocdOffset, token).ConfigureAwait(false);
            if (U16(tail, offset + 8) != U16(tail, offset + 10))
                throw new InvalidDataException("Invalid ZIP entry count.");
            return ValidateRange(U32(tail, offset + 16), U32(tail, offset + 12), U16(tail, offset + 10), eocdOffset);
        }
        throw new InvalidDataException("ZIP directory end is missing.");
    }

    private static async Task<DirectoryRange> ReadZip64DirectoryAsync(Stream source, long endOffset, CancellationToken token)
    {
        if (endOffset < 20)
            throw new InvalidDataException("ZIP64 locator is missing.");
        source.Position = endOffset - 20;
        var locator = new byte[20];
        await source.ReadExactlyAsync(locator, token).ConfigureAwait(false);
        if (U32(locator, 0) != 0x07064b50 || U32(locator, 4) != 0 || U32(locator, 16) != 1)
            throw new InvalidDataException("Invalid ZIP64 locator.");
        var zip64Offset = I64(locator, 8);
        if (zip64Offset > endOffset - 76)
            throw new InvalidDataException("Invalid ZIP64 directory offset.");
        source.Position = zip64Offset;
        var end = new byte[56];
        await source.ReadExactlyAsync(end, token).ConfigureAwait(false);
        if (U32(end, 0) != 0x06064b50 || I64(end, 4) < 44 || I64(end, 4) > endOffset - 32 - zip64Offset ||
            U32(end, 16) != 0 || U32(end, 20) != 0 || I64(end, 24) != I64(end, 32))
            throw new InvalidDataException("Invalid ZIP64 directory end.");
        return ValidateRange(I64(end, 48), I64(end, 40), I64(end, 32), zip64Offset);
    }

    private static DirectoryRange ValidateRange(long offset, long length, long count, long end)
    {
        if (offset > end || length > end - offset || count > length / 46)
            throw new InvalidDataException("Invalid ZIP central directory range.");
        return new(offset, length, count);
    }

    private static ushort U16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
    private static uint U32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
    private static long I64(byte[] data, int offset)
    {
        var value = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset));
        return value <= long.MaxValue ? (long)value : throw new InvalidDataException("ZIP value exceeds supported range.");
    }

}
