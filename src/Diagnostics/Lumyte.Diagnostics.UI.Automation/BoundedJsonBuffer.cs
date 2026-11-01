using System.Buffers;

namespace Lumyte.Diagnostics.UI.Automation;

internal sealed class BoundedJsonBuffer(int limit) : IBufferWriter<byte>
{
    private readonly ArrayBufferWriter<byte> _buffer = new(Math.Min(limit, 4096));

    public void Advance(int count)
    {
        if (count < 0 || count > limit - _buffer.WrittenCount)
        {
            throw new InvalidOperationException("result-too-large");
        }

        _buffer.Advance(count);
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        if (sizeHint < 0 || Math.Max(sizeHint, 1) > limit - _buffer.WrittenCount)
        {
            throw new InvalidOperationException("result-too-large");
        }

        Memory<byte> memory = _buffer.GetMemory(sizeHint);
        return memory[..Math.Min(memory.Length, limit - _buffer.WrittenCount)];
    }

    public Span<byte> GetSpan(int sizeHint = 0) => GetMemory(sizeHint).Span;

    internal byte[] ToArray() => _buffer.WrittenSpan.ToArray();
}
