namespace Lumyte.Graphics.Wgpu;

internal sealed class DescriptorRegistration : GpuResource
{
    internal DescriptorRegistration(WgpuDevice owner, uint identity, GpuResource resource, BufferSlice range = default)
        : base(owner)
    {
        (Identity, Resource, Range) = (identity, resource, range);
        resource.Acquire();
    }

    internal uint Identity { get; }

    internal GpuResource Resource { get; }

    internal BufferSlice Range { get; }

    protected override void ReleaseNative() => Resource.ReleaseLease();
}
