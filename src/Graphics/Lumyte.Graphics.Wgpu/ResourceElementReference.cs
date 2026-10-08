namespace Lumyte.Graphics.Wgpu;

internal sealed class ResourceElementReference<T>(DescriptorRegistration registration, ulong count, ulong stride = 0, ShaderDataRegion? region = null) : ResourceReference, IGpuRef<T>
{
    public ulong Count { get; } = count;

    internal override DescriptorRegistration Registration { get; } = registration;

    internal override ShaderDataRegion? Region { get; } = region;

    public IGpuRef<T> GetElement(ulong index)
    {
        lock (Registration.Owner.Gate)
        {
            Registration.Check(Registration.Owner);
            if (index >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            if (stride == 0 || Count == 1)
            {
                Check(Registration.Owner);
                return this;
            }

            ulong offset = checked(Registration.Range.Offset + (index * stride));
            ShaderDataRegion? element = null;
            if (Region is not null)
            {
                element = new(Region.Buffer, offset, stride, Region.Snapshot, checked(Region.FirstElement + (int)index)) { Ready = true };
                element.Check(Registration.Owner);
            }

            DescriptorRegistration selected = Registration.GetElement(offset, stride);
            return new ResourceElementReference<T>(selected, 1, stride, element);
        }
    }
}
