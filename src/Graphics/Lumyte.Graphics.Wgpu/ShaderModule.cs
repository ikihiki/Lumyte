using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

internal sealed class ShaderModule : GpuResource
{
    internal ShaderModule(WgpuDevice owner, A.ShaderModule native, ShaderDataSchema? schema)
        : base(owner)
    {
        (Native, ShaderDataSchema) = (native, schema);
    }

    internal A.ShaderModule Native { get; }

    internal ShaderDataSchema? ShaderDataSchema { get; }

    protected override void ReleaseNative() => Native.Dispose();
}
