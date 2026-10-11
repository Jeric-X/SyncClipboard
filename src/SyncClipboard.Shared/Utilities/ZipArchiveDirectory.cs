using System.Buffers.Binary;
using System.IO.Compression;

namespace SyncClipboard.Shared.Utilities;

internal static class ZipArchiveDirectory
{
    private sealed record DirectoryRange(long Offset, long Length, long Count);

    public static async Task ValidateAsync(Stream source, uint? limit, CancellationToken token)
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
            if (source.Position > end - 6)
                throw new InvalidDataException("Truncated ZIP central directory.");
            await source.ReadExactlyAsync(header.AsMemory(0, 6), token).ConfigureAwait(false);
            if (U32(header, 0) == 0x05054b50)
            {
                if (count != directory.Count || U16(header, 4) != end - source.Position)
                    throw new InvalidDataException("Invalid ZIP directory signature.");
                source.Position = end;
                break;
            }
            CheckEntryCount(++count, limit);
            if (source.Position > end - (header.Length - 6))
                throw new InvalidDataException("Truncated ZIP central directory.");
            await source.ReadExactlyAsync(header.AsMemory(6), token).ConfigureAwait(false);
            if (U32(header, 0) != 0x02014b50)
                throw new InvalidDataException("Invalid ZIP directory entry.");
            var length = (long)U16(header, 28) + U16(header, 30) + U16(header, 32);
            if (length > end - source.Position)
                throw new InvalidDataException("Truncated ZIP directory entry.");
            var next = source.Position + length;
            await ValidateDirectoryAsync(source, header, directory.Offset, token).ConfigureAwait(false);
            source.Position = next;
        }
        if (count != directory.Count)
            throw new InvalidDataException("ZIP directory entry count mismatch.");
        source.Position = 0;
    }

    private static async Task ValidateDirectoryAsync(Stream source, byte[] header, long directoryOffset, CancellationToken token)
    {
        var nameLength = U16(header, 28);
        if (nameLength == 0)
            return;
        source.Position += nameLength - 1;
        var last = new byte[1];
        await source.ReadExactlyAsync(last, token).ConfigureAwait(false);
        if (last[0] != (byte)'/')
            return;
        var extra = new byte[U16(header, 30)];
        await source.ReadExactlyAsync(extra, token).ConfigureAwait(false);
        var (size, compressedSize, offset) = GetDirectoryDataRange(header, extra);
        if (size != 0)
            throw new InvalidDataException("Group directory entries cannot contain payload data.");
        if (compressedSize == 0)
            return;
        await ValidateDirectoryStreamAsync(source, header, compressedSize, offset, directoryOffset, token).ConfigureAwait(false);
    }

    private static (long Size, long CompressedSize, long Offset) GetDirectoryDataRange(byte[] header, byte[] extra)
    {
        long size = U32(header, 24), compressedSize = U32(header, 20), offset = U32(header, 42);
        if (size != uint.MaxValue && compressedSize != uint.MaxValue && offset != uint.MaxValue)
            return (size, compressedSize, offset);
        for (var i = 0; i <= extra.Length - 4;)
        {
            var length = U16(extra, i + 2);
            if (length > extra.Length - i - 4)
                break;
            if (U16(extra, i) == 1)
            {
                var end = i + 4 + length;
                i += 4;
                if (size == uint.MaxValue)
                    size = ReadZip64Value(extra, ref i, end);
                if (compressedSize == uint.MaxValue)
                    compressedSize = ReadZip64Value(extra, ref i, end);
                if (offset == uint.MaxValue)
                    offset = ReadZip64Value(extra, ref i, end);
                return (size, compressedSize, offset);
            }
            i += 4 + length;
        }
        throw new InvalidDataException("ZIP64 directory entry metadata is missing.");
    }

    private static long ReadZip64Value(byte[] extra, ref int offset, int end)
    {
        if (offset > end - 8)
            throw new InvalidDataException("Truncated ZIP64 entry metadata.");
        var value = I64(extra, offset);
        offset += 8;
        return value;
    }

    private static async Task ValidateDirectoryStreamAsync(
        Stream source, byte[] header, long compressedSize, long offset, long directoryOffset, CancellationToken token)
    {
        if (offset > directoryOffset - 30 || (U16(header, 8) & 1) != 0)
            throw new InvalidDataException("Invalid ZIP directory local header.");
        source.Position = offset;
        var local = new byte[30];
        await source.ReadExactlyAsync(local, token).ConfigureAwait(false);
        if (U32(local, 0) != 0x04034b50 || U16(local, 8) != U16(header, 10))
            throw new InvalidDataException("Invalid ZIP directory local header.");
        var start = source.Position + U16(local, 26) + U16(local, 28);
        if (start > directoryOffset || compressedSize > directoryOffset - start)
            throw new InvalidDataException("Invalid ZIP directory data range.");
        source.Position = start;
        using var bounded = new EntryDataStream(source, compressedSize);
        using Stream decoded = U16(header, 10) switch
        {
            0 => bounded,
            8 => new DeflateStream(bounded, CompressionMode.Decompress),
            _ => throw new InvalidDataException("Unsupported ZIP directory compression.")
        };
        if (await decoded.ReadAsync(new byte[1], token).ConfigureAwait(false) != 0)
            throw new InvalidDataException("Group directory entries cannot contain payload data.");
    }

    private sealed class EntryDataStream(Stream source, long remaining) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = source.Read(buffer, offset, (int)Math.Min(count, remaining));
            remaining -= read;
            return read;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            var read = await source.ReadAsync(buffer[..(int)Math.Min(buffer.Length, remaining)], token).ConfigureAwait(false);
            remaining -= read;
            return read;
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static void CheckEntryCount(long count, uint? limit)
    {
        if (limit.HasValue && count > limit.Value)
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
            if (U32(tail, offset) != 0x06054b50 || offset + 22 + U16(tail, offset + 20) > tail.Length)
                continue;
            var eocdOffset = source.Length - tail.Length + offset;
            if (U16(tail, offset + 4) != 0 || U16(tail, offset + 6) != 0)
                throw new InvalidDataException("Split ZIP archives are unsupported.");
            if (U32(tail, offset + 12) == uint.MaxValue || U32(tail, offset + 16) == uint.MaxValue ||
                (U16(tail, offset + 10) == ushort.MaxValue && await HasZip64LocatorAsync(source, eocdOffset, token).ConfigureAwait(false)))
                return await ReadZip64DirectoryAsync(source, eocdOffset, token).ConfigureAwait(false);
            if (U16(tail, offset + 8) != U16(tail, offset + 10))
                throw new InvalidDataException("Invalid ZIP entry count.");
            return ValidateRange(U32(tail, offset + 16), U32(tail, offset + 12), U16(tail, offset + 10), eocdOffset);
        }
        throw new InvalidDataException("ZIP directory end is missing.");
    }

    private static async Task<bool> HasZip64LocatorAsync(Stream source, long endOffset, CancellationToken token)
    {
        if (endOffset < 20)
            return false;
        source.Position = endOffset - 20;
        var signature = new byte[4];
        await source.ReadExactlyAsync(signature, token).ConfigureAwait(false);
        return U32(signature, 0) == 0x07064b50;
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
