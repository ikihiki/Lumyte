namespace Lumyte.Graphics.Direct3D12;

internal sealed unsafe partial class Direct3D12ComputePipeline
{
    private nint _native;

    internal nint Native
    {
        get
        {
            ValidateAlive();
            return _native;
        }
    }

    private void Initialize()
    {
        byte[] code = _compute.Native;
        fixed (byte* bytes = code)
        {
            _owner.Check(NativeMethods.ComputeCreate(_owner.Native, bytes, (ulong)code.Length, out _native));
        }
    }

    private void DisposeNative() => NativeMethods.PipelineDestroy(_native);
}
