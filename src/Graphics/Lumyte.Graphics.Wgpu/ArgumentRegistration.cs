namespace Lumyte.Graphics.Wgpu;

internal sealed class ArgumentRegistration(WgpuArgumentTable owner, uint slot, object resource, Action release)
{
    private bool _active = true;

    internal WgpuArgumentTable Owner { get; } = owner;

    internal uint Slot { get; } = slot;

    internal object Resource { get; } = resource;

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
