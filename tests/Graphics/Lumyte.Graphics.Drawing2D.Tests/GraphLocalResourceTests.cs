using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Mathematics.Colors;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Guards pass-local prepared storage against false externally observable writes.</summary>
public sealed class GraphLocalResourceTests
{
    /// <summary>Unobserved transient drawing does not survive only because its private roots receive uploads.</summary>
    /// <returns>The asynchronous preparation and recording completion.</returns>
    [Fact]
    public async Task DeadDrawingBatchDoesNotBecomeAnExternalBufferWriteAsync()
    {
        var device = new ProfilingDevice();
        using var graph = new Graph(device);
        var description = new TextureDesc { Width = 16, Height = 16, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled };
        RenderGraphTexture target = graph.CreateTexture(description);
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        canvas.FillCircle(new(8), 3, new SrgbRgba<float>(1, 0, 0, 1));
        using var renderer = new RenderGraphRenderer2D(DrawingCommandRenderers2D.CreateDefault(), canvas.End(), new() { Texture = target, Description = description, LoadOp = AttachmentLoadOp.Clear }, new DrawingGraphBindings2D(graph));
        using PreparedDrawingPasses2D prepared = await graph.Append2D(renderer);
        using CompiledRenderGraph compiled = graph.Compile();
        Assert.DoesNotContain("drawing2d.batch", compiled.PassNames);
        Assert.DoesNotContain("drawing2d.initialize-shared-fallback", compiled.PassNames);
        using var commands = new ProfilingCommands();
        compiled.Record(commands);
        Assert.Empty(device.Tables);
        commands.Dispose();
    }

    /// <summary>Pass-confined allocations cannot be accessed, exported or promoted from outside their owning pass.</summary>
    [Fact]
    public void LocalStorageCannotEscapeItsDeclaredPass()
    {
        var device = new ProfilingDevice();
        using var graph = new Graph(device);
        using IGraphicsBuffer<uint> buffer = device.CreateBuffer<uint>(new() { Count = 4, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead });
        RenderGraphPass local = graph.AddPass("local", _ => { });
        local.UseLocalBuffer(buffer, new(PipelineStage.Copy | PipelineStage.ComputeShader, ResourceAccess.CopyWrite | ResourceAccess.ShaderRead));
        RenderGraphBuffer<uint> handle = graph.GetOrImportBuffer(buffer, default);
        RenderGraphPass other = graph.AddPass("other", _ => { });
        Assert.Throws<ArgumentException>(() => other.UseBuffer(handle, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead)));
        Assert.Throws<ArgumentException>(() => other.UseLocalBuffer(buffer, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead)));
        Assert.Throws<ArgumentException>(() => graph.MarkOutput(handle));
        Assert.Throws<ArgumentException>(() => graph.ExportBuffer(handle, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead)));
        using CompiledRenderGraph compiled = graph.Compile();
        Assert.Empty(compiled.PassNames);
    }

    /// <summary>Using local syntax cannot strip an already global import of observable write side effects.</summary>
    [Fact]
    public void ExistingGlobalImportRetainsItsOriginalObservability()
    {
        var device = new ProfilingDevice();
        using var graph = new Graph(device);
        using IGraphicsBuffer<uint> buffer = device.CreateBuffer<uint>(new() { Count = 4, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead });
        RenderGraphBuffer<uint> imported = graph.ImportBuffer(buffer, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead));
        RenderGraphPass pass = graph.AddPass("observable", _ => { });
        pass.UseLocalBuffer(buffer, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        Assert.Same(imported, graph.GetOrImportBuffer(buffer, default));
        using CompiledRenderGraph compiled = graph.Compile();
        Assert.Contains("observable", compiled.PassNames);
    }

    /// <summary>A base-class reference cannot append or create siblings after the graph has compiled.</summary>
    [Fact]
    public void BaseCastCannotMutateCompiledGraphShaderResources()
    {
        var device = new ProfilingDevice();
        using var graph = new Graph(device);
        ShaderResources resources = graph.CreatePreparedPayload();
        using var sampler = new DispatchSampler();
        graph.AddPass("operation", _ => { }).KeepAlive().AttachPreparedPayload(resources);
        using CompiledRenderGraph compiled = graph.Compile();
        Assert.Throws<InvalidOperationException>(() => resources.RegisterSampler(sampler, _ => { }));
        Assert.Throws<InvalidOperationException>(() => resources.CreateSibling());
        Assert.Throws<InvalidOperationException>(() => resources.Cancel());
        Assert.Throws<InvalidOperationException>(() => resources.WhenReady(() => { }));
    }
}
