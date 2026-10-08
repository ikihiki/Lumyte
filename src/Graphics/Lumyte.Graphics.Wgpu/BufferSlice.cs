namespace Lumyte.Graphics.Wgpu;

internal readonly struct BufferSlice
{
    internal BufferSlice(WgpuBuffer buffer, ulong offset, ulong length)
    {
        (Buffer, Offset, Length) = (buffer, offset, length);
    }

    public WgpuBuffer Buffer { get; }

    public ulong Offset { get; }

    public ulong Length { get; }
}
