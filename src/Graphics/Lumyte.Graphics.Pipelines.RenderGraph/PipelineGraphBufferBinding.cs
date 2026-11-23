using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Pipelines.RenderGraph;

/// <summary>A closed typed association of one manifest slot with an imported native buffer.</summary>
public sealed class PipelineGraphBufferBinding
{
    private readonly Association _association;

    private PipelineGraphBufferBinding(PipelineResourceSlot slot, Association association)
    {
        ArgumentNullException.ThrowIfNull(slot);
        Slot = slot;
        _association = association;
    }

    /// <summary>Gets the opaque manifest-local slot.</summary>
    public PipelineResourceSlot Slot { get; }

    /// <summary>Captures an exact typed native allocation and imported handle.</summary>
    /// <typeparam name="TElement">The unmanaged canonical storage type.</typeparam>
    /// <param name="slot">The exact manifest slot.</param>
    /// <param name="resource">The already imported logical buffer.</param>
    /// <param name="allocation">The actual native buffer allocation.</param>
    /// <param name="scope">The complete actual access union.</param>
    /// <returns>The closed typed binding.</returns>
    public static PipelineGraphBufferBinding Create<TElement>(PipelineResourceSlot slot, RenderGraphBuffer<TElement> resource, IGraphicsBuffer<TElement> allocation, BarrierScope scope)
        where TElement : unmanaged
        => new(slot, new Typed<TElement>(resource, allocation, scope));

    internal RenderGraphPassUse Validate(Lumyte.Graphics.RenderGraph.RenderGraph graph, PipelineExternalResource resource, IGraphicDevice device)
        => _association.Validate(graph, resource, device);

    private abstract class Association
    {
        internal abstract RenderGraphPassUse Validate(Lumyte.Graphics.RenderGraph.RenderGraph graph, PipelineExternalResource resource, IGraphicDevice device);
    }

    private sealed class Typed<TElement>(RenderGraphBuffer<TElement> resource, IGraphicsBuffer<TElement> allocation, BarrierScope scope) : Association
        where TElement : unmanaged
    {
        internal override RenderGraphPassUse Validate(Lumyte.Graphics.RenderGraph.RenderGraph graph, PipelineExternalResource expected, IGraphicDevice device)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(allocation);
            if (expected is not PipelineBufferResource<TElement> typed || !ReferenceEquals(typed.Slice.Buffer, allocation) || typed.Scope != scope)
            {
                throw new ArgumentException("Buffer binding differs from the captured allocation, element type, range or scope.");
            }

            BufferSlice<TElement> actual = graph.GetImportedBuffer(resource, typed.Slice.Offset, typed.Slice.Count, device);
            if (!ReferenceEquals(actual.Buffer, allocation) || actual.Offset != typed.Slice.Offset || actual.Count != typed.Slice.Count)
            {
                throw new ArgumentException("Imported buffer does not match the actual native range.");
            }

            return RenderGraphPassUse.Buffer(resource, typed.Slice.Offset, typed.Slice.Count, scope);
        }
    }
}
