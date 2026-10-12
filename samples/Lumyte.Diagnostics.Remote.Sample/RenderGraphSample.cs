using Lumyte.Diagnostics.RenderGraph;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Diagnostics.Remote.Sample;

internal static class RenderGraphSample
{
    internal static Graph Create(RenderGraphDiagnostics observer)
    {
        var graph = new Graph(new CompileOnlyDevice(), "sample-frame", observer);
        var copyWrite = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyWrite);
        var copyRead = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead);
        var computeRead = new BarrierScope(PipelineStage.ComputeShader, ResourceAccess.ShaderRead);
        var computeWrite = new BarrierScope(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite);
        var vertexRead = new BarrierScope(PipelineStage.VertexShader, ResourceAccess.ShaderRead);
        var sampledRead = new BarrierScope(PipelineStage.FragmentShader, ResourceAccess.ShaderRead);
        var colorWrite = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);
        var colorReadWrite = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
        var depthWrite = new BarrierScope(PipelineStage.DepthStencil, ResourceAccess.DepthStencilWrite);
        var depthRead = new BarrierScope(PipelineStage.DepthStencil, ResourceAccess.DepthStencilRead);

        RenderGraphBuffer<uint> scene = graph.CreateBuffer(new BufferDesc<uint> { Count = 1024, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead }, "scene-data");
        RenderGraphBuffer<uint> visible = graph.CreateBuffer(new BufferDesc<uint> { Count = 256, Usage = BufferUsage.ShaderRead | BufferUsage.ShaderWrite }, "visible-instances");
        RenderGraphTexture depth = CreateTexture(graph, "depth", TextureFormat.Depth32Float);
        RenderGraphTexture shadow = CreateTexture(graph, "shadow-depth", TextureFormat.Depth32Float);
        RenderGraphTexture gbuffer = CreateTexture(graph, "gbuffer");
        RenderGraphTexture hdr = CreateTexture(graph, "hdr-color");
        RenderGraphTexture bloomA = CreateTexture(graph, "bloom-a");
        RenderGraphTexture bloomB = CreateTexture(graph, "bloom-b");
        RenderGraphTexture post = CreateTexture(graph, "post-color");
        RenderGraphTexture grade = CreateTexture(graph, "graded-color");
        RenderGraphTexture ui = CreateTexture(graph, "ui-color");
        RenderGraphTexture output = CreateTexture(graph, "frame-output");
        RenderGraphBuffer<uint> debug = graph.CreateBuffer(new BufferDesc<uint> { Count = 16, Usage = BufferUsage.ShaderWrite }, "unused-debug-data");

        graph.AddPass("obsolete-scene-upload", UnsupportedRecording).UseBuffer(scene, copyWrite);
        graph.AddPass("upload-scene", UnsupportedRecording).UseBuffer(scene, copyWrite, discard: true);
        graph.AddPass("cull-instances", UnsupportedRecording).UseBuffer(scene, computeRead).UseBuffer(visible, computeWrite, discard: true);
        graph.AddPass("depth-prepass", UnsupportedRecording).UseBuffer(visible, vertexRead).UseTexture(depth, TextureState.DepthStencilAttachment, depthWrite, discard: true);
        graph.AddPass("shadow-map", UnsupportedRecording).UseBuffer(visible, vertexRead).UseTexture(shadow, TextureState.DepthStencilAttachment, depthWrite, discard: true);
        graph.AddPass("gbuffer", UnsupportedRecording)
            .UseBuffer(visible, vertexRead)
            .UseTexture(depth, TextureState.DepthStencilAttachment, depthRead)
            .UseTexture(gbuffer, TextureState.ColorAttachment, colorWrite, discard: true);
        graph.AddPass("lighting", UnsupportedRecording)
            .UseTexture(gbuffer, TextureState.Sampled, sampledRead)
            .UseTexture(shadow, TextureState.Sampled, sampledRead)
            .UseTexture(hdr, TextureState.ColorAttachment, colorWrite, discard: true);
        graph.AddPass("sky", UnsupportedRecording).UseTexture(hdr, TextureState.ColorAttachment, colorReadWrite);
        graph.AddPass("particles", UnsupportedRecording).UseTexture(hdr, TextureState.ColorAttachment, colorReadWrite);
        graph.AddPass("bloom-threshold", UnsupportedRecording).UseTexture(hdr, TextureState.Sampled, sampledRead).UseTexture(bloomA, TextureState.ColorAttachment, colorWrite, discard: true);
        graph.AddPass("bloom-horizontal", UnsupportedRecording).UseTexture(bloomA, TextureState.Sampled, sampledRead).UseTexture(bloomB, TextureState.ColorAttachment, colorWrite, discard: true);
        graph.AddPass("bloom-vertical", UnsupportedRecording).UseTexture(bloomB, TextureState.Sampled, sampledRead).UseTexture(bloomA, TextureState.ColorAttachment, colorWrite, discard: true);
        graph.AddPass("tonemap", UnsupportedRecording)
            .UseTexture(hdr, TextureState.Sampled, sampledRead)
            .UseTexture(bloomA, TextureState.Sampled, sampledRead)
            .UseTexture(post, TextureState.ColorAttachment, colorWrite, discard: true);
        graph.AddPass("color-grade", UnsupportedRecording).UseTexture(post, TextureState.Sampled, sampledRead).UseTexture(grade, TextureState.ColorAttachment, colorWrite, discard: true);
        graph.AddPass("ui-base", UnsupportedRecording).UseTexture(grade, TextureState.Sampled, sampledRead).UseTexture(ui, TextureState.ColorAttachment, colorWrite, discard: true);
        graph.AddPass("ui-overlay", UnsupportedRecording).UseTexture(ui, TextureState.ColorAttachment, colorReadWrite);
        RenderGraphPass copy = graph.AddPass("final-copy", UnsupportedRecording).UseTexture(ui, TextureState.CopySource, copyRead).UseTexture(output, TextureState.CopyDestination, copyWrite, discard: true);
        graph.AddPass("frame-marker", UnsupportedRecording).DependsOn(copy).KeepAlive();
        graph.AddPass("unused-debug", UnsupportedRecording).UseBuffer(debug, computeWrite);
        graph.ExportTexture(output, TextureState.Sampled, sampledRead);
        return graph;
    }

    private static RenderGraphTexture CreateTexture(Graph graph, string name, TextureFormat format = TextureFormat.Rgba8Unorm)
    {
        var desc = new TextureDesc
        {
            Width = 1280,
            Height = 720,
            Format = format,
            Usage = TextureUsage.CopySource | TextureUsage.CopyDestination | TextureUsage.Sampled | TextureUsage.RenderAttachment,
        };
        return graph.CreateTexture(desc, name);
    }

    private static void UnsupportedRecording(RenderGraphContext context) => throw new NotSupportedException("This sample provides declarations for compile diagnostics only.");
}
