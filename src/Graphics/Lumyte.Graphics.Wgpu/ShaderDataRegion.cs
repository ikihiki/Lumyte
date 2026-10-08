namespace Lumyte.Graphics.Wgpu;

internal sealed class ShaderDataRegion(WgpuBuffer buffer, ulong offset, ulong length, ShaderDataSnapshot snapshot, int firstElement = 0)
{
    internal WgpuBuffer Buffer { get; } = buffer;

    internal ulong Offset { get; } = offset;

    internal ulong Length { get; } = length;

    internal ShaderDataSnapshot Snapshot { get; } = snapshot;

    internal int FirstElement { get; } = firstElement;

    internal bool Valid { get; set; } = true;

    internal bool Ready { get; set; }

    internal void Check(WgpuDevice owner)
    {
        Buffer.Check(owner);
        Snapshot.Check(owner);
        if (!Valid || !Ready || !Buffer.IsCurrent(this))
        {
            throw new ArgumentException("Shader data was overwritten or its metadata was invalidated.");
        }
    }
}
