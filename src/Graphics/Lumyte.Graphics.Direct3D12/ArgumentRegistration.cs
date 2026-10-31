namespace Lumyte.Graphics.Direct3D12;

internal sealed class ArgumentRegistration(Direct3D12ArgumentTable owner, uint slot, object resource, ulong offset, ulong size)
{
    internal Direct3D12ArgumentTable Owner { get; } = owner;

    internal uint Slot { get; } = slot;

    internal object Resource { get; } = resource;

    internal ulong OffsetInBytes { get; } = offset;

    internal ulong SizeInBytes { get; } = size;
}
