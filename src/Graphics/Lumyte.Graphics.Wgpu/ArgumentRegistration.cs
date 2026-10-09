namespace Lumyte.Graphics.Wgpu;

internal sealed class ArgumentRegistration(WgpuArgumentTable owner, object resource, Action release)
{
    private bool _active = true;

    internal object Resource { get; } = resource;

    internal void Check()
    {
        owner.ThrowIfDisposed();
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
