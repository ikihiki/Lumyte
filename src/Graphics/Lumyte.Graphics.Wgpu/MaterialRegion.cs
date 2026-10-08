namespace Lumyte.Graphics.Wgpu;

internal sealed class MaterialRegion(WgpuBuffer buffer, ulong offset, ulong length, MaterialBindings bindings)
{
    internal WgpuBuffer Buffer { get; } = buffer;

    internal ulong Offset { get; } = offset;

    internal ulong Length { get; } = length;

    internal MaterialBindings Bindings { get; } = bindings;

    internal bool Valid { get; set; } = true;

    internal bool Ready { get; set; }

    internal void Check(WgpuDevice owner)
    {
        Buffer.Check(owner);
        Bindings.Check(owner);
        if (!Valid || !Ready)
        {
            throw new ArgumentException("Material data was overwritten or its registration was invalidated.");
        }
    }
}
