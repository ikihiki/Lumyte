using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Identifies a whole imported or temporary typed buffer without owning its allocation.</summary>
/// <typeparam name="T">The unmanaged storage element type.</typeparam>
public sealed class RenderGraphBuffer<T> : RenderGraphResource
    where T : unmanaged
{
    private readonly BufferDesc<T>? _desc;

    internal RenderGraphBuffer(RenderGraph owner, IGraphicsBuffer<T> buffer, BarrierScope scope, string name)
        : base(owner, buffer, TextureState.Undefined, scope, name)
    {
    }

    internal RenderGraphBuffer(RenderGraph owner, BufferDesc<T> desc, string name)
        : base(owner, null, TextureState.Undefined, default, name)
    {
        _desc = desc with { };
    }

    internal override string Kind => "Buffer";

    private BufferUsage Usage => Allocation is IGraphicsBuffer<T> buffer ? buffer.Usage : _desc!.Usage;

    private MemoryPreference Memory => Allocation is IGraphicsBuffer<T> buffer ? buffer.Memory : _desc!.Memory;

    internal override IDisposable Create(IGraphicDevice device) => device.CreateBuffer(_desc!);

    internal override void Validate(TextureState state, BarrierScope scope)
    {
        ResourceAccess access = scope.Access;
        if (Conflicts(access, ResourceAccess.CopyRead, BufferUsage.CopySource) ||
            Conflicts(access, ResourceAccess.CopyWrite, BufferUsage.CopyDestination) ||
            Conflicts(access, ResourceAccess.ShaderRead, BufferUsage.ShaderRead) ||
            Conflicts(access, ResourceAccess.ShaderWrite, BufferUsage.ShaderWrite) ||
            Conflicts(access, ResourceAccess.IndexRead, BufferUsage.Index) ||
            Conflicts(access, ResourceAccess.IndirectRead, BufferUsage.Indirect) ||
            ((access & ResourceAccess.HostWrite) != 0 && Memory != MemoryPreference.Upload) ||
            ((access & ResourceAccess.HostRead) != 0 && Memory != MemoryPreference.Readback) ||
            (access & (ResourceAccess.ColorRead | ResourceAccess.ColorWrite | ResourceAccess.DepthStencilRead | ResourceAccess.DepthStencilWrite)) != 0)
        {
            throw new ArgumentException("Buffer usage or memory conflicts with its access.");
        }
    }

    internal override void Barrier(IGraphicsCommandBuffer commands, object allocation, TextureState beforeState, TextureState afterState, BarrierScope before, BarrierScope after)
        => commands.Barrier(new BufferBarrierDesc<T>
        {
            Buffer = ((IGraphicsBuffer<T>)allocation).Slice(0, ((IGraphicsBuffer<T>)allocation).Count),
            Before = before,
            After = after,
        });

    private bool Conflicts(ResourceAccess access, ResourceAccess operation, BufferUsage usage)
        => (access & operation) != 0 && (Usage & usage) == 0;
}
