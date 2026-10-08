namespace Lumyte.Graphics.Wgpu;

internal sealed class ShaderDataSnapshot : GpuResource
{
    internal ShaderDataSnapshot(WgpuDevice owner, ShaderDataSchema schema, Type dataType, DescriptorRegistration[][] elements)
        : base(owner)
    {
        (Schema, DataType, Elements) = (schema, dataType, elements);
        foreach (DescriptorRegistration registration in elements.SelectMany(element => element).Distinct())
        {
            registration.Acquire();
        }
    }

    internal ShaderDataSchema Schema { get; }

    internal Type DataType { get; }

    internal DescriptorRegistration[][] Elements { get; }

    internal override void ReleaseLease()
    {
        base.ReleaseLease();
        if (ActiveLeases == 0)
        {
            Dispose();
        }
    }

    protected override void ReleaseNative()
    {
        foreach (DescriptorRegistration registration in Elements.SelectMany(element => element).Distinct())
        {
            registration.ReleaseLease();
        }
    }
}
