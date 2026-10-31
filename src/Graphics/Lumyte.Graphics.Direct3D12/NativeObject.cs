namespace Lumyte.Graphics.Direct3D12;

internal sealed class NativeObject(nint handle) : IDisposable
{
    private nint _handle = handle;

    public void Dispose()
    {
        NativeMethods.Release(_handle);
        _handle = 0;
    }
}
