using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

internal sealed class ShaderModule : GpuResource
{
    internal ShaderModule(WgpuDevice owner, A.ShaderModule native, MaterialSchema? schema)
        : base(owner)
    {
        (Native, MaterialSchema) = (native, schema);
    }

    internal A.ShaderModule Native { get; }

    internal MaterialSchema? MaterialSchema { get; }

    protected override void ReleaseNative() => Native.Dispose();
}
