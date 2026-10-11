using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Identifies an imported typed buffer without owning its allocation.</summary>
/// <typeparam name="T">The unmanaged storage element type.</typeparam>
public sealed class RenderGraphBuffer<T> : RenderGraphResource
    where T : unmanaged
{
    internal RenderGraphBuffer(RenderGraph owner, IGraphicsBuffer<T> buffer, BarrierScope scope)
        : base(owner, buffer, TextureState.Undefined, scope)
    {
    }

    internal IGraphicsBuffer<T> Buffer => (IGraphicsBuffer<T>)Allocation;

    internal override void Validate(TextureState state, BarrierScope scope)
    {
        ResourceAccess access = scope.Access;
        if (Conflicts(access, ResourceAccess.CopyRead, BufferUsage.CopySource) ||
            Conflicts(access, ResourceAccess.CopyWrite, BufferUsage.CopyDestination) ||
            Conflicts(access, ResourceAccess.ShaderRead, BufferUsage.ShaderRead) ||
            Conflicts(access, ResourceAccess.ShaderWrite, BufferUsage.ShaderWrite) ||
            Conflicts(access, ResourceAccess.IndexRead, BufferUsage.Index) ||
            Conflicts(access, ResourceAccess.IndirectRead, BufferUsage.Indirect) ||
            ((access & ResourceAccess.HostWrite) != 0 && Buffer.Memory != MemoryPreference.Upload) ||
            ((access & ResourceAccess.HostRead) != 0 && Buffer.Memory != MemoryPreference.Readback) ||
            (access & (ResourceAccess.ColorRead | ResourceAccess.ColorWrite | ResourceAccess.DepthStencilRead | ResourceAccess.DepthStencilWrite)) != 0)
        {
            throw new ArgumentException("Buffer usage or memory conflicts with its access.");
        }
    }

    internal override void Barrier(IGraphicsCommandBuffer commands, TextureState beforeState, TextureState afterState, BarrierScope before, BarrierScope after)
        => commands.Barrier(new BufferBarrierDesc<T>
        {
            Buffer = Buffer.Slice(0, Buffer.Count),
            Before = before,
            After = after,
        });

    private bool Conflicts(ResourceAccess access, ResourceAccess operation, BufferUsage usage)
        => (access & operation) != 0 && (Buffer.Usage & usage) == 0;
}
