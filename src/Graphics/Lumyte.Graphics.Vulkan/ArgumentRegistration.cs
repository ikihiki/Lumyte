namespace Lumyte.Graphics.Vulkan;

internal sealed class ArgumentRegistration(VulkanArgumentTable owner, uint slot, object resource, Action release, ulong offset, ulong size)
{
    private bool _active = true;

    internal VulkanArgumentTable Owner { get; } = owner;

    internal uint Slot { get; } = slot;

    internal object Resource { get; } = resource;

    internal ulong OffsetInBytes { get; } = offset;

    internal ulong SizeInBytes { get; } = size;

    internal void Check()
    {
        Owner.ThrowIfDisposed();
        if (!_active)
        {
            throw new InvalidOperationException("The argument table registration has been replaced or released.");
        }
    }

    internal void Release()
    {
        if (!_active)
        {
            return;
        }

        _active = false;
        release();
    }
}
