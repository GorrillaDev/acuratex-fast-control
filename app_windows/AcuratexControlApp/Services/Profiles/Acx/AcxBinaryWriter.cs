using System.Buffers.Binary;
using System.Text;

namespace AcuratexControlApp.Services.Profiles.Acx;

internal sealed class AcxBinaryWriter : IDisposable
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private readonly Stream _stream;
    private bool _disposed;

    public AcxBinaryWriter(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        if (!_stream.CanWrite) {
            throw new ArgumentException("Stream must be writable.", nameof(stream));
        }
    }

    public long Position => _stream.Position;

    public void WriteByte(byte value)
    {
        EnsureNotDisposed();
        _stream.WriteByte(value);
    }

    public void WriteUInt16(ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        Write(buffer);
    }

    public void WriteInt32(int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        Write(buffer);
    }

    public void WriteUInt32(uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        Write(buffer);
    }

    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        EnsureNotDisposed();
        if (value.Length == 0) {
            return;
        }

        byte[] buffer = value.ToArray();
        _stream.Write(buffer, 0, buffer.Length);
    }

    public ushort WriteLengthPrefixedUtf8(string value, int maxLengthBytes)
    {
        if (value is null) {
            throw new ArgumentNullException(nameof(value));
        }

        int byteCount = Utf8.GetByteCount(value);
        if (byteCount > ushort.MaxValue || byteCount > maxLengthBytes) {
            throw new InvalidOperationException($"String exceeds the maximum supported length of {Math.Min(ushort.MaxValue, maxLengthBytes)} bytes.");
        }

        WriteUInt16((ushort)byteCount);
        if (byteCount == 0) {
            return 0;
        }

        byte[] bytes = Utf8.GetBytes(value);
        _stream.Write(bytes, 0, bytes.Length);
        return (ushort)byteCount;
    }

    public void WritePadding(int count)
    {
        EnsureNotDisposed();
        if (count <= 0) {
            return;
        }

        Span<byte> zero = stackalloc byte[16];
        while (count > 0) {
            int chunk = Math.Min(count, zero.Length);
            _stream.Write(zero.Slice(0, chunk));
            count -= chunk;
        }
    }

    public void Dispose()
    {
        if (_disposed) {
            return;
        }

        _disposed = true;
        _stream.Dispose();
    }

    private void Write(ReadOnlySpan<byte> value)
    {
        EnsureNotDisposed();
        if (value.Length == 0) {
            return;
        }

        byte[] buffer = value.ToArray();
        _stream.Write(buffer, 0, buffer.Length);
    }

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
