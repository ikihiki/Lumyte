using System.Diagnostics;

using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Library;

public static class ComputeRenderGraphExtensions
{
    public static GpuRenderGraphPassBuilder AddCompute(
        this GpuRenderGraph graph,
        string name,
        ComputeData compute,
        bool markWrittenBuffersAsOutputs = true)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return Add(graph.AddPass, graph.MarkOutput, name, compute, markWrittenBuffersAsOutputs);
    }

    public static GpuRenderGraphPassBuilder AddCompute(
        this GpuRenderGraphContributionContext context,
        string name,
        ComputeData compute,
        bool markWrittenBuffersAsOutputs = true)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Add(context.AddPass, context.MarkOutput, name, compute, markWrittenBuffersAsOutputs);
    }

    private static GpuRenderGraphPassBuilder Add(
        AddPassDelegate addPass,
        Func<GpuRenderGraphBuffer, object> markOutput,
        string name,
        ComputeData compute,
        bool markWrittenBuffersAsOutputs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(compute);
        GpuRenderGraphPassBuilder builder = addPass(
            name,
            compute,
            static (context, value) => Record(context, value),
            GpuRenderGraphPassFlags.None);
        foreach (ComputeBufferBinding buffer in compute.Buffers)
        {
            _ = buffer.Access switch
            {
                GpuRenderGraphAccess.Read => builder.Read(buffer.Buffer, GpuStage.ComputeShader),
                GpuRenderGraphAccess.Write => builder.Write(buffer.Buffer, GpuStage.ComputeShader),
                GpuRenderGraphAccess.ReadWrite => builder.ReadWrite(buffer.Buffer, GpuStage.ComputeShader),
                _ => throw new UnreachableException(),
            };
            if (markWrittenBuffersAsOutputs && (buffer.Access & GpuRenderGraphAccess.Write) != 0)
            {
                _ = markOutput(buffer.Buffer);
            }
        }
        return builder;
    }

    private static void Record(GpuRenderGraphPassContextView context, ComputeData compute)
    {
        int readOnlySlotCount = RequiredSlotCount(compute.Buffers, writable: false);
        int writableSlotCount = RequiredSlotCount(compute.Buffers, writable: true);
        var resources = new GpuResourceTable(
            textureSlotCount: 0,
            samplerSlotCount: 0,
            bufferSlotCount: readOnlySlotCount,
            storageTextureSlotCount: 0,
            writableBufferSlotCount: writableSlotCount);
        foreach (ComputeBufferBinding buffer in compute.Buffers)
        {
            bool writable = buffer.Access != GpuRenderGraphAccess.Read;
            GpuBufferView view = context.GetBufferView(
                buffer.Buffer,
                new(Access: writable ? GpuBufferViewAccess.ReadWrite : GpuBufferViewAccess.ReadOnly));
            if (writable)
            {
                resources.SetWritableBuffer(checked((int)buffer.Slot), view.Id);
            }
            else
            {
                resources.SetBuffer(checked((int)buffer.Slot), view.Id);
            }
        }
        context.Commands
            .SetComputePipeline(compute.Pipeline)
            .SetComputeResourceTable(resources)
            .Dispatch(
                compute.Dispatch.GroupCountX,
                compute.Dispatch.GroupCountY,
                compute.Dispatch.GroupCountZ);
    }

    private static int RequiredSlotCount(
        IReadOnlyList<ComputeBufferBinding> buffers,
        bool writable)
    {
        uint? maximum = null;
        foreach (ComputeBufferBinding buffer in buffers)
        {
            if ((buffer.Access != GpuRenderGraphAccess.Read) != writable) { continue; }
            maximum = maximum is null ? buffer.Slot : Math.Max(maximum.Value, buffer.Slot);
        }
        return maximum is null ? 0 : checked((int)maximum.Value + 1);
    }

    private delegate GpuRenderGraphPassBuilder AddPassDelegate(
        string name,
        ComputeData state,
        GpuRenderGraphPassAction<ComputeData> record,
        GpuRenderGraphPassFlags flags);
}
