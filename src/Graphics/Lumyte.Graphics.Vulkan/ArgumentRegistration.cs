namespace Lumyte.Graphics.Vulkan;

internal sealed class ArgumentRegistration(VulkanArgumentTable owner, uint slot, object resource, ulong offset, ulong size)
{
    internal VulkanArgumentTable Owner { get; } = owner;

    internal uint Slot { get; } = slot;

    internal object Resource { get; } = resource;

    internal ulong OffsetInBytes { get; } = offset;

    internal ulong SizeInBytes { get; } = size;
}
