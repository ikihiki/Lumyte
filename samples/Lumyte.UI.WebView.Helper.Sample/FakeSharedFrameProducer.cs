using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using Lumyte.UI.Application.WebView.Cef;

// Development-only producer. Exercises the real mapped ABI, never linked into product code.
internal sealed unsafe class FakeSharedFrameProducer : IDisposable
{
    private readonly MemoryMappedFile _map;
    private readonly MemoryMappedViewAccessor _view;
    private readonly byte[] _nonce;
    private readonly long _stride;
    private readonly int _capacity;
    private byte* _pointer;
    private long _sequence;

    internal FakeSharedFrameProducer(string root)
    {
        _map = MemoryMappedFile.CreateFromFile(Path.Combine(root, "frames-v1.shm"), FileMode.Open);
        _view = _map.CreateViewAccessor();
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref _pointer);
        ReadOnlySpan<byte> header = new(_pointer, 4096);
        _capacity = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(header[24..]));
        _stride = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(header[32..]));
        _nonce = header[48..64].ToArray();
    }

    internal byte[] ReadyBody => [.. CefSharedFrameLayout.Magic, .. _nonce];

    public void Dispose()
    {
        _view.SafeMemoryMappedViewHandle.ReleasePointer();
        _pointer = null;
        _view.Dispose();
        _map.Dispose();
    }

    internal bool Publish(int width, int height, long generation, byte value = 127)
    {
        int bytes = checked(width * height * 4);
        if (bytes > _capacity)
        {
            throw new InvalidDataException("fixture-frame-capacity");
        }

        int selected = -1;
        long oldest = long.MaxValue;
        for (int i = 0; i < 3; i++)
        {
            byte* candidate = _pointer + 4096 + (i * _stride);
            int state = Volatile.Read(ref *(int*)candidate);
            if (state == 0 && Interlocked.CompareExchange(ref *(int*)candidate, 1, 0) == 0)
            {
                selected = i;
                break;
            }

            long sequence = Interlocked.Read(ref *(long*)(candidate + 8));
            if (state == 2 && sequence < oldest)
            {
                oldest = sequence;
                selected = i;
            }
        }

        if (selected < 0)
        {
            return false;
        }

        byte* pointer = _pointer + 4096 + (selected * _stride);
        if (Volatile.Read(ref *(int*)pointer) != 1 && Interlocked.CompareExchange(ref *(int*)pointer, 1, 2) != 2)
        {
            return false;
        }

        Span<byte> slot = new(pointer, 64);
        BinaryPrimitives.WriteInt32LittleEndian(slot[4..], selected);
        BinaryPrimitives.WriteInt64LittleEndian(slot[16..], generation);
        BinaryPrimitives.WriteInt32LittleEndian(slot[24..], width);
        BinaryPrimitives.WriteInt32LittleEndian(slot[28..], height);
        BinaryPrimitives.WriteInt32LittleEndian(slot[32..], width * 4);
        BinaryPrimitives.WriteInt32LittleEndian(slot[36..], bytes);
        _nonce.CopyTo(slot[40..56]);
        BinaryPrimitives.WriteInt32LittleEndian(slot[56..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(slot[60..], 0);
        new Span<byte>(pointer + 64, bytes).Fill(value);
        Interlocked.Exchange(ref *(long*)(pointer + 8), checked(++_sequence));
        Volatile.Write(ref *(int*)pointer, 2);
        return true;
    }
}
