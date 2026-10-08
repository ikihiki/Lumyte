namespace Lumyte.Graphics.Wgpu;

internal abstract class ResourceReference
{
    internal abstract DescriptorRegistration Registration { get; }

    internal abstract ShaderDataRegion? Region { get; }

    internal void Check(WgpuDevice owner)
    {
        Registration.Check(owner);
        Registration.Resource.Check(owner);
        Region?.Check(owner);
    }
}
