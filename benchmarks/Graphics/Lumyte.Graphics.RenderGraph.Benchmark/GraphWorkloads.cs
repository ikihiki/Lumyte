using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Benchmarks.Testing;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.RenderGraph.Benchmark.Compilation;

/// <summary>Public fixtures shared by deterministic correctness tests, with no native backend.</summary>
public static class GraphWorkloads
{
    /// <summary>Builds a live copy graph and a deliberately dead branch for compiler culling.</summary>
    /// <param name="device">The borrowed CPU recording device.</param>
    /// <param name="source">The initialized imported source allocation.</param>
    /// <param name="passCount">The positive number of copies.</param>
    /// <param name="shape">The dependency topology.</param>
    /// <param name="order">An optional recording-order oracle.</param>
    /// <returns>The caller-owned graph.</returns>
    public static Graph Build(CpuGraphicsDevice device, IGraphicsBuffer<uint> source, int passCount, GraphShape shape, IList<int>? order = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(passCount);
        var graph = new Graph(device);
        BarrierScope read = new(PipelineStage.Copy, ResourceAccess.CopyRead);
        BarrierScope write = new(PipelineStage.Copy, ResourceAccess.CopyWrite);
        RenderGraphBuffer<uint> imported = graph.ImportBuffer(source, read, "source");
        RenderGraphBuffer<uint> previous = imported;
        RenderGraphPass? previousPass = null;
        var branches = new List<RenderGraphPass>();
        for (int i = 0; i < passCount; i++)
        {
            int ordinal = i;
            RenderGraphBuffer<uint> input = shape == GraphShape.FanOutJoin ? imported : previous;
            RenderGraphBuffer<uint> output = shape == GraphShape.ImportedAlias
                ? graph.GetOrImportBuffer(source, read, $"alias-{i}")
                : graph.CreateBuffer(new BufferDesc<uint> { Count = 64, Usage = BufferUsage.CopySource | BufferUsage.CopyDestination }, $"temporary-{i}");
            RenderGraphPass pass = graph.AddPass($"copy-{i}", context =>
            {
                order?.Add(ordinal);
                ulong inputOffset = shape == GraphShape.ImportedAlias ? (ulong)(ordinal % 2) * 32 : 0;
                ulong outputOffset = shape == GraphShape.ImportedAlias ? (ulong)((ordinal + 1) % 2) * 32 : 0;
                ulong count = shape == GraphShape.ImportedAlias ? 32UL : 64UL;
                context.Commands.CopyBuffer(context.GetBuffer(input).SliceElements(inputOffset, count), context.GetBuffer(output).SliceElements(outputOffset, count));
            });
            if (ReferenceEquals(input, output))
            {
                pass.UseBuffer(output, new(PipelineStage.Copy, ResourceAccess.CopyRead | ResourceAccess.CopyWrite));
            }
            else
            {
                pass.UseBuffer(input, read).UseBuffer(output, write);
            }

            if (shape == GraphShape.ImportedAlias && previousPass is not null)
            {
                pass.DependsOn(previousPass);
            }

            if (shape == GraphShape.FanOutJoin)
            {
                branches.Add(pass);
                graph.MarkOutput(output);
            }

            previous = output;
            previousPass = pass;
        }

        if (shape == GraphShape.FanOutJoin)
        {
            RenderGraphPass join = graph.AddPass("join", _ => order?.Add(passCount)).KeepAlive();
            foreach (RenderGraphPass branch in branches)
            {
                join.DependsOn(branch);
            }
        }

        graph.ExportBuffer(previous, read);
        RenderGraphBuffer<uint> dead = graph.CreateBuffer(new BufferDesc<uint> { Count = 64, Usage = BufferUsage.CopyDestination }, "dead");
        graph.AddPass("dead", _ => throw new InvalidOperationException("Culled branch was recorded.")).UseBuffer(dead, write);
        return graph;
    }
}
