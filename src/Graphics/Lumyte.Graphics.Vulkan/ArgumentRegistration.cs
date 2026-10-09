namespace Lumyte.Graphics.Vulkan;

internal sealed class ArgumentRegistration
{
    private readonly VulkanArgumentTable _owner;
    private readonly Action _release;
    private bool _active = true;

    internal ArgumentRegistration(VulkanArgumentTable owner, object resource, Action release)
    {
        (_owner, Resource, _release) = (owner, resource, release);
    }

    internal object Resource { get; }

    internal void Check()
    {
        _owner.ThrowIfDisposed();
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
        _release();
    }
}
