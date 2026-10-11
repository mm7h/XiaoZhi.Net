using System.Buffers;

namespace XiaoZhi.Net.Server.Media.Common.Buffers;

/// <summary>
/// 使用池化数组保存可顺序写入和读取的字节数据。
/// </summary>
internal sealed class PooledByteBufferWriter : IBufferWriter<byte>, IDisposable
{
    private byte[]? _buffer;
    private int _readOffset;
    private int _writeOffset;
    private bool _disposed;

    public int Count => this._writeOffset - this._readOffset;

    public void Advance(int count)
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);

        if (count < 0 || this._buffer is null || count > this._buffer.Length - this._writeOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        this._writeOffset += count;
    }

    public void Clear()
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);
        this._readOffset = 0;
        this._writeOffset = 0;
    }

    public void Dispose()
    {
        if (this._disposed)
        {
            return;
        }

        this._disposed = true;

        if (this._buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(this._buffer);
            this._buffer = null;
        }

        this._readOffset = 0;
        this._writeOffset = 0;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        this.EnsureCapacity(sizeHint);
        return this._buffer.AsMemory(this._writeOffset);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        this.EnsureCapacity(sizeHint);
        return this._buffer.AsSpan(this._writeOffset);
    }

    public byte[] Read(int count)
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);

        if (count < 0 || count > this.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (count == 0)
        {
            return [];
        }

        byte[] result = GC.AllocateUninitializedArray<byte>(count);
        this._buffer.AsSpan(this._readOffset, count).CopyTo(result);
        this._readOffset += count;

        if (this._readOffset == this._writeOffset)
        {
            this._readOffset = 0;
            this._writeOffset = 0;
        }

        return result;
    }

    public byte[] ReadRemaining()
    {
        return this.Read(this.Count);
    }

    private void EnsureCapacity(int sizeHint)
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);

        if (sizeHint < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeHint));
        }

        sizeHint = Math.Max(1, sizeHint);

        if (this._buffer is null)
        {
            this._buffer = ArrayPool<byte>.Shared.Rent(sizeHint);
            return;
        }

        if (this._buffer.Length - this._writeOffset >= sizeHint)
        {
            return;
        }

        int dataLength = this.Count;
        if (this._readOffset > 0)
        {
            this._buffer.AsSpan(this._readOffset, dataLength).CopyTo(this._buffer);
            this._readOffset = 0;
            this._writeOffset = dataLength;

            if (this._buffer.Length - this._writeOffset >= sizeHint)
            {
                return;
            }
        }

        int requiredCapacity = checked(dataLength + sizeHint);
        int doubledCapacity = this._buffer.Length <= int.MaxValue / 2
            ? this._buffer.Length * 2
            : int.MaxValue;
        byte[] expandedBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(requiredCapacity, doubledCapacity));
        this._buffer.AsSpan(this._readOffset, dataLength).CopyTo(expandedBuffer);
        ArrayPool<byte>.Shared.Return(this._buffer);
        this._buffer = expandedBuffer;
        this._readOffset = 0;
        this._writeOffset = dataLength;
    }
}
