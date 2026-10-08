namespace Lumyte.Graphics.Wgpu;

internal sealed class DescriptorRegistration : GpuResource
{
    private readonly Dictionary<(ulong Offset, ulong Length), DescriptorRegistration> _elements = [];

    internal DescriptorRegistration(WgpuDevice owner, uint identity, GpuResource resource, BufferSlice range = default)
        : base(owner)
    {
        (Identity, Resource, Range) = (identity, resource, range);
        resource.Acquire();
    }

    internal uint Identity { get; }

    internal GpuResource Resource { get; }

    internal BufferSlice Range { get; }

    internal DescriptorRegistration GetElement(ulong offset, ulong length)
    {
        Check(Owner);
        if (!_elements.TryGetValue((offset, length), out DescriptorRegistration? element))
        {
            var range = new BufferSlice((WgpuBuffer)Resource, offset, length);
            element = new(Owner, Owner.NextDescriptorIdentity(Resource, range), Resource, range);
            _elements.Add((offset, length), element);
        }

        return element;
    }

    internal void RequireRegistrationIdle()
    {
        RequireIdle();
        foreach (DescriptorRegistration element in _elements.Values)
        {
            element.RequireRegistrationIdle();
        }
    }

    protected override void ReleaseNative()
    {
        RequireRegistrationIdle();
        foreach (DescriptorRegistration element in _elements.Values)
        {
            element.Dispose();
        }

        _elements.Clear();
        Resource.ReleaseLease();
    }
}
