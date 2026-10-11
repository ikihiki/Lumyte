namespace Lumyte.Graphics.Wgpu;

internal sealed class ArgumentRegistration(WgpuArgumentTable owner, uint slot, object resource, ulong offset, ulong size)
{
    internal WgpuArgumentTable Owner { get; } = owner;

    internal uint Slot { get; } = slot;

    internal object Resource { get; } = resource;

    internal ulong OffsetInBytes { get; } = offset;

    internal ulong SizeInBytes { get; } = size;
}
