using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace SyncClipboard.Core.Utilities.History.HistoryImport;

// Scan the central directory without asking ZipArchive to materialize unrelated entries.
internal static class HistoryImportArchive
{
    private sealed record DirectoryRange(long Offset, long Length, long Count);

    public static async Task<ZipArchive> OpenAsync(Stream source, IReadOnlySet<string> paths, CancellationToken token)
    {
        var directory = await ReadDirectoryRangeAsync(source, token).ConfigureAwait(false);
        using var filtered = new MemoryStream();
        var copies = new Dictionary<string, int>(StringComparer.Ordinal);
        var header = new byte[46];
        source.Position = directory.Offset;
        var end = checked(directory.Offset + directory.Length);
        long count = 0;
        for (long i = 0; i < directory.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            if (source.Position > end - header.Length)
                throw new InvalidDataException("Truncated ZIP central directory.");
            await source.ReadExactlyAsync(header, token).ConfigureAwait(false);
            if (U32(header, 0) != 0x02014b50)
                throw new InvalidDataException("Invalid ZIP directory entry.");
            var nameLength = U16(header, 28);
            var extraLength = U16(header, 30);
            var commentLength = U16(header, 32);
            if (source.Position > end - nameLength - extraLength - commentLength)
                throw new InvalidDataException("Truncated ZIP directory entry.");
            var name = new byte[nameLength];
            await source.ReadExactlyAsync(name, token).ConfigureAwait(false);
            var path = Encoding.UTF8.GetString(name);
            copies.TryGetValue(path, out var seen);
            if (!paths.Contains(path) || seen >= 2)
            {
                source.Position += extraLength + commentLength;
                continue;
            }
            // Two copies suffice to retain duplicate-path detection, without retaining an unbounded duplicate list.
            copies[path] = seen + 1;
            var extra = new byte[extraLength];
            await source.ReadExactlyAsync(extra, token).ConfigureAwait(false);
            source.Position += commentLength;
            var zip64Extra = GetZip64Extra(extra);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(30), (ushort)zip64Extra.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(32), 0);
            filtered.Write(header);
            filtered.Write(name);
            filtered.Write(zip64Extra);
            count++;
        }
        if (source.Position != end)
            throw new InvalidDataException("ZIP directory size does not match its entries.");
        WriteDirectoryEnd(filtered, directory.Offset, count);
        token.ThrowIfCancellationRequested();
        var view = new DirectoryView(source, directory.Offset, filtered.ToArray());
        try
        {
            return new ZipArchive(view, ZipArchiveMode.Read, leaveOpen: false);
        }
        catch
        {
            view.Dispose();
            throw;
        }
    }

    private static byte[] GetZip64Extra(byte[] extra)
    {
        for (var offset = 0; offset <= extra.Length - 4;)
        {
            var length = U16(extra, offset + 2);
            if (offset + 4 + length > extra.Length)
                throw new InvalidDataException("Invalid ZIP extra field.");
            if (U16(extra, offset) == 1)
                return extra.AsSpan(offset, length + 4).ToArray();
            offset += length + 4;
        }
        return [];
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

    private static void WriteDirectoryEnd(MemoryStream output, long offset, long count)
    {
        var size = output.Length;
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write(0x06064b50U);
        writer.Write(44L);
        writer.Write((ushort)45);
        writer.Write((ushort)45);
        writer.Write(0U);
        writer.Write(0U);
        writer.Write(count);
        writer.Write(count);
        writer.Write(size);
        writer.Write(offset);
        writer.Write(0x07064b50U);
        writer.Write(0U);
        writer.Write(checked(offset + size));
        writer.Write(1U);
        writer.Write(0x06054b50U);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(ushort.MaxValue);
        writer.Write(ushort.MaxValue);
        writer.Write(uint.MaxValue);
        writer.Write(uint.MaxValue);
        writer.Write((ushort)0);
    }

    private static ushort U16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
    private static uint U32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
    private static long I64(byte[] data, int offset)
    {
        var value = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset));
        return value <= long.MaxValue ? (long)value : throw new InvalidDataException("ZIP value exceeds supported range.");
    }

    // Keep original local-header/data offsets; replace only the central directory and its end records.
    private sealed class DirectoryView(Stream source, long directoryOffset, byte[] directory) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => checked(directoryOffset + directory.LongLength);
        public override long Position { get; set; }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            if (Position < 0)
                throw new IOException("Invalid ZIP position.");
            if (Position >= Length)
                return 0;
            int read;
            if (Position < directoryOffset)
            {
                source.Position = Position;
                read = source.Read(buffer[..(int)Math.Min(buffer.Length, directoryOffset - Position)]);
            }
            else
            {
                read = (int)Math.Min(buffer.Length, Length - Position);
                directory.AsSpan((int)(Position - directoryOffset), read).CopyTo(buffer);
            }
            Position += read;
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin) => Position = checked(offset + (origin switch
        {
            SeekOrigin.Begin => 0,
            SeekOrigin.Current => Position,
            SeekOrigin.End => Length,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        }));
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
