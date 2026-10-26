using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Identifies an imported shader data allocation including its transfer metadata.</summary>
/// <typeparam name="T">The application shader data structure.</typeparam>
public sealed class RenderGraphShaderDataBuffer<T> : RenderGraphResource
    where T : struct, IShaderData
{
    internal RenderGraphShaderDataBuffer(RenderGraph owner, IGraphicsShaderDataBuffer<T> buffer, BarrierScope scope, string name)
        : base(owner, buffer, TextureState.Undefined, scope, name)
    {
    }

    internal override string Kind => "ShaderDataBuffer";

    internal override IDisposable Create(IGraphicDevice device) => throw new InvalidOperationException("Shader data is imported.");

    internal override void Validate(TextureState state, BarrierScope scope)
    {
        var buffer = (IGraphicsShaderDataBuffer<T>)Allocation!;
        ResourceAccess allowed = buffer.Memory == MemoryPreference.Upload
            ? ResourceAccess.HostWrite | ResourceAccess.CopyRead
            : ResourceAccess.CopyWrite | ResourceAccess.ShaderRead;
        if ((scope.Access & ~allowed) != 0)
        {
            throw new ArgumentException("Shader data access conflicts with its memory preference.");
        }
    }

    internal override void Barrier(IGraphicsCommandBuffer commands, object allocation, TextureState beforeState, TextureState afterState, BarrierScope before, BarrierScope after)
    {
        var buffer = (IGraphicsShaderDataBuffer<T>)allocation;
        commands.Barrier(new ShaderDataBufferBarrierDesc<T>
        {
            Buffer = buffer.SliceElements(0, buffer.Count),
            Before = before,
            After = after,
        });
    }
}
