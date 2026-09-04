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
        for (int index = 1; index < this.buffers.Length; index++)
        {
            if (this.buffers[index - 1].Slot == this.buffers[index].Slot)
            {
                throw new ArgumentException("Compute buffer slots must be unique.", nameof(buffers));
            }
        }
    }

    public GpuComputePipelineHandle Pipeline { get; }
    public ComputeDispatch Dispatch { get; }
    public IReadOnlyList<ComputeBufferBinding> Buffers => buffers;
}
