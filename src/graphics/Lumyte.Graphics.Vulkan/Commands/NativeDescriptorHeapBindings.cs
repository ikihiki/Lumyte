using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal unsafe struct NativeDescriptorHeapBindings
{
    public NativeBindHeapInfo? Resource;
    public NativeBindHeapInfo? Sampler;

    public readonly void Apply(CommandBuffer command,
        delegate* unmanaged<CommandBuffer, NativeBindHeapInfo*, void> bindResource,
        delegate* unmanaged<CommandBuffer, NativeBindHeapInfo*, void> bindSampler)
    {
        if (Resource is { } resource) { bindResource(command, &resource); }
        if (Sampler is { } sampler) { bindSampler(command, &sampler); }
    }
}
