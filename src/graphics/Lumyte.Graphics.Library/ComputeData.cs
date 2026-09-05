using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Library;

public sealed class ComputeData
{
    private readonly ComputeBufferBinding[] buffers;

    public ComputeData(
        GpuComputePipelineHandle pipeline,
        ComputeDispatch dispatch,
        IEnumerable<ComputeBufferBinding>? buffers = null)
    {
        if (pipeline.IsNull) { throw new ArgumentException("Pipeline cannot be null.", nameof(pipeline)); }
        Pipeline = pipeline;
        Dispatch = dispatch.Validate();
        this.buffers = buffers?.OrderBy(static value => value.Slot).ToArray() ?? [];
        foreach (ComputeBufferBinding buffer in this.buffers) { buffer.Validate(); }
        foreach (IGrouping<bool, ComputeBufferBinding> table in this.buffers.GroupBy(IsWritable))
        {
            ComputeBufferBinding[] bindings = table.OrderBy(static value => value.Slot).ToArray();
            for (int index = 1; index < bindings.Length; index++)
            {
                if (bindings[index - 1].Slot == bindings[index].Slot)
                {
                    throw new ArgumentException(
                        "Compute buffer slots must be unique within each read-only or writable table.",
                        nameof(buffers));
                }
            }
        }
    }

    public GpuComputePipelineHandle Pipeline { get; }
    public ComputeDispatch Dispatch { get; }
    public IReadOnlyList<ComputeBufferBinding> Buffers => buffers;

    private static bool IsWritable(ComputeBufferBinding binding)
        => binding.Access != GpuRenderGraphAccess.Read;
}
