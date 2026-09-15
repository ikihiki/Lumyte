using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Native.RenderGraph;
using Lumyte.Graphics.Native.Resources;
using Lumyte.Graphics.Native.Shaders;
using Lumyte.Graphics.Passes;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Text;
using Lumyte.Graphics.TwoD;
using Lumyte.Graphics.Native.Passes.TwoD.Generated;
using DxRoot = Lumyte.Graphics.Native.Passes.TwoD.Generated.DirectX12.Root;
using VkRoot = Lumyte.Graphics.Native.Passes.TwoD.Generated.Vulkan.Root;

namespace Lumyte.Graphics.Native.Passes;

public static class Native2DRendering
{
    public static NativeRenderPassRegistry Add2DRendering(this NativeRenderPassRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Register(Draw2DPassContract.Instance, static services => new NativeDraw2DPass(services));
        return registry;
    }
}

/// <summary>Native scene renderer with immutable geometry uploads, bindless image references and explicit graph intermediates.</summary>
public sealed partial class NativeDraw2DPass(NativePassServices services) : INativeRenderPass<Draw2DPassRequest, Draw2DPassResult>
{
    private readonly Dictionary<GpuFormat, NativeGpuRasterPipelineHandle> pipelines = [];
    private readonly Dictionary<(Draw2DScene Scene, uint Width, uint Height), CachedScene> scenes = [];
    private readonly LinkedList<(Draw2DScene Scene, uint Width, uint Height)> sceneOrder = [];
    private readonly Dictionary<GpuImageUploadData, CachedImage> images = new(ReferenceEqualityComparer.Instance);
    private readonly LinkedList<GpuImageUploadData> imageOrder = [];
    private readonly Dictionary<PreparationKey, IReadOnlyList<Node>> preparedNodes = [];
    private readonly Dictionary<NativePreparedDraw, CachedBuffer> drawBuffers = new(ReferenceEqualityComparer.Instance);
    private readonly LinkedList<NativePreparedDraw> bufferOrder = [];

    public async ValueTask BuildAsync(NativePassBuildContext context, Draw2DPassRequest request, Draw2DPassResult result,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NativePassTexture target = context.ImportTexture(request.Color);
        Draw2DScene scene = context.GetInput(request.Scene);
        var key = (scene, target.Description.Width, target.Description.Height);
        if (!scenes.TryGetValue(key, out CachedScene? prepared))
        {
            prepared = new(PrepareScene(scene, key.Width, key.Height));
            scenes.Add(key, prepared);
            sceneOrder.AddLast(key);
            if (sceneOrder.Count > 32)
            { var oldest = sceneOrder.First!.Value; sceneOrder.RemoveFirst(); scenes.Remove(oldest); }
        }
        else
        { sceneOrder.Remove(key); sceneOrder.AddLast(key); }
        var execution = new BuildState(context, cancellationToken);
        await DrawNodes(execution, prepared.Nodes, target);
        // The feature's ReadWrite contract remains true even for an empty or fully transparent scene.
        context.AddPass(execution.Name("finish"), target, static (record, texture) => { })
            .ReadWrite(target, new(GpuStage.ColorOutput, GpuAccess.ColorRead | GpuAccess.ColorWrite));
    }

    private async ValueTask DrawNodes(BuildState build, IReadOnlyList<Node> nodes, NativePassTexture target)
    {
        foreach (Node node in nodes)
        {
            build.Cancellation.ThrowIfCancellationRequested();
            if (node is DrawNode draw)
            { await Draw(build, draw.Draw, target); continue; }
            if (node is ClipNode clip)
            {
                NativePassTexture working = CreateIntermediate(build, target, "clip-group");
                Copy(build, target, working);
                await DrawNodes(build, clip.Children, working);
                await Draw(build, clip.Composite, target, working);
                continue;
            }
            var layer = (LayerNode)node;
            NativePassTexture content = CreateIntermediate(build, target, "layer");
            Clear(build, content);
            await DrawNodes(build, layer.Children, content);
            NativePassTexture original = content;
            if (layer.Options.BlurRadius > 0)
            { content = Blur(build, content, layer.Options.BlurRadius * layer.Scale); }
            if (layer.Shadow is { } shadow)
            {
                NativePassTexture shadowImage = layer.Options.Shadow!.BlurRadius > 0
                    ? Blur(build, original, layer.Options.Shadow.BlurRadius * layer.Scale) : original;
                await Draw(build, shadow, target, shadowImage);
            }
            await Draw(build, layer.Composite, target, content);
        }
    }

    private async ValueTask Draw(BuildState build, NativePreparedDraw draw, NativePassTexture target, NativePassTexture? imageOverride = null)
    {
        NativePassTexture? image = imageOverride ?? (draw.Image is { } source ? await ImportImage(build, source) : null);
        NativePassTexture? mask = draw.Mask is { } maskSource ? await ImportImage(build, maskSource) : null;
        if (!drawBuffers.TryGetValue(draw, out CachedBuffer? buffer))
        {
            byte[] bytes = MemoryMarshal.AsBytes(draw.Data.AsSpan()).ToArray();
            GpuResourceScope scope = services.Resources.CreateScope();
            try
            {
                GpuBufferRef data = scope.CreateBuffer(new((ulong)bytes.Length, NativeGpuMemoryKind.CpuVisible, 16));
                WriteMapped(services.Resources.GetBufferRange(data), bytes);
                GpuViewRef view = scope.GetView(data, new GpuBufferViewDescription(0, (ulong)bytes.Length));
                buffer = new(scope, data, view);
                drawBuffers.Add(draw, buffer);
                bufferOrder.AddLast(draw);
            }
            catch { scope.Dispose(); throw; }
            if (bufferOrder.Count > 1024)
            { var oldest = bufferOrder.First!.Value; bufferOrder.RemoveFirst(); drawBuffers.Remove(oldest, out var retired); retired!.Scope.Dispose(); }
        }
        else
        { bufferOrder.Remove(draw); bufferOrder.AddLast(draw); }
        NativePassBuffer sceneData = build.Context.ImportBuffer(buffer.Buffer);
        build.Context.Retain(services.Resources.AcquireUse(buffer.View));
        NativePassTexture backdrop = CreateIntermediate(build, target, "backdrop");
        Copy(build, target, backdrop);
        NativePassView targetView = View(build, target, true), backdropView = View(build, backdrop);
        NativePassView? imageView = image is null ? null : View(build, image);
        NativePassView? maskView = mask is null ? null : View(build, mask);
        var state = new DrawState(Pipeline(target.Description.Format), targetView, backdropView, imageView, maskView,
            sceneData, services.Resources.GetShaderIndex(buffer.View), 0, 0, 0, services.Backend.ShaderCodeFormat);
        var pass = build.Context.AddPass(build.Name("draw"), state, Record)
            .Read(sceneData, new(GpuStage.PixelShader, GpuAccess.ShaderRead))
            .Read(backdrop, new(GpuStage.PixelShader, GpuAccess.ShaderRead))
            .Write(target, new(GpuStage.ColorOutput, GpuAccess.ColorWrite));
        if (image is not null)
        { pass.Read(image, new(GpuStage.PixelShader, GpuAccess.ShaderRead)); }
        if (mask is not null)
        { pass.Read(mask, new(GpuStage.PixelShader, GpuAccess.ShaderRead)); }
    }

    private NativePassTexture Blur(BuildState build, NativePassTexture source, float radius)
    {
        for (int axis = 0; axis < 2; axis++)
        {
            NativePassTexture destination = CreateIntermediate(build, source, "blur");
            var state = new DrawState(Pipeline(destination.Description.Format), View(build, destination, true), null,
                View(build, source), null, null, 0, 1, radius / 3, axis, services.Backend.ShaderCodeFormat);
            build.Context.AddPass(build.Name("blur"), state, Record)
                .Read(source, new(GpuStage.PixelShader, GpuAccess.ShaderRead))
                .Write(destination, new(GpuStage.ColorOutput, GpuAccess.ColorWrite));
            source = destination;
        }
        return source;
    }

    private void Copy(BuildState build, NativePassTexture source, NativePassTexture destination)
    {
        var state = new DrawState(Pipeline(destination.Description.Format), View(build, destination, true), null,
            View(build, source), null, null, 0, 2, 0, 0, services.Backend.ShaderCodeFormat);
        build.Context.AddPass(build.Name("copy"), state, Record)
            .Read(source, new(GpuStage.PixelShader, GpuAccess.ShaderRead))
            .Write(destination, new(GpuStage.ColorOutput, GpuAccess.ColorWrite));
    }

    private NativePassTexture CreateIntermediate(BuildState build, NativePassTexture target, string name)
        => build.Context.CreateTexture(build.Name(name), target.Description with
        { Usage = NativeGpuTextureUsage.Sampled | NativeGpuTextureUsage.ColorAttachment });

    private static void Clear(BuildState build, NativePassTexture target)
    {
        NativePassView attachment = View(build, target, true);
        build.Context.AddPass(build.Name("clear"), attachment, static (record, view) =>
        { record.Commands.BeginRendering([new(record.GetRenderView(view), NativeGpuLoadOp.Clear)]); record.Commands.EndRendering(); })
            .Write(target, new(GpuStage.ColorOutput, GpuAccess.ColorWrite));
    }
    private static NativePassView View(BuildState build, NativePassTexture texture, bool attachment = false)
        => build.Context.CreateView(build.Name(attachment ? "attachment" : "sampled"), texture,
            new(NativeGpuTextureViewDimension.TwoD, texture.Description.Format, NativeGpuTextureAspect.Color, 0, 1, 0, 1,
                attachment ? GpuTextureViewPurpose.Attachment : GpuTextureViewPurpose.Sampled));

    private NativeGpuRasterPipelineHandle Pipeline(GpuFormat format)
    {
        if (pipelines.TryGetValue(format, out var existing))
        { return existing; }
        using NativeShaderProgram program = services.ShaderLoader.Load(ShaderPackage.Create(),
            services.Backend.ShaderCodeFormat == GpuShaderCodeFormat.Dxil ? DxRoot.AbiHash : VkRoot.AbiHash);
        var pipeline = services.Backend.CreateRasterPipeline(new() { ColorTargets = [new(format)], SampleCount = 1 }, program.Code);
        try
        { pipelines.Add(format, pipeline); return pipeline; }
        catch { services.Backend.DestroyRasterPipeline(pipeline); throw; }
    }

    private static void Record(NativePassRecordContext record, DrawState state)
    {
        if (state.Data is not null)
        { record.Commands.Barrier(GpuStage.Host, GpuAccess.HostWrite, GpuStage.PixelShader, GpuAccess.ShaderRead); }
        record.Commands.BeginRendering([new(record.GetRenderView(state.Target), NativeGpuLoadOp.Discard)]);
        record.Commands.SetPipeline(state.Pipeline);
        uint backdrop = state.Backdrop is null ? 0 : record.GetShaderIndex(state.Backdrop);
        uint image = state.Image is null ? 0 : record.GetShaderIndex(state.Image);
        uint mask = state.Mask is null ? 0 : record.GetShaderIndex(state.Mask);
        if (state.CodeFormat == GpuShaderCodeFormat.Dxil)
        {
            DxRoot root = new()
            {
                data = state.DataIndex,
                backdrop = backdrop,
                image = image,
                mask = mask,
                operation = state.Operation,
                sigma = state.Sigma,
                axis = state.Axis
            };
            record.Commands.Draw(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref root, 1)), 3);
        }
        else
        {
            VkRoot root = new()
            {
                data = state.DataIndex,
                backdrop = backdrop,
                image = image,
                mask = mask,
                operation = state.Operation,
                sigma = state.Sigma,
                axis = state.Axis
            };
            record.Commands.Draw(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref root, 1)), 3);
        }
        record.Commands.EndRendering();
    }
    private static void WriteMapped(NativeGpuRange range, byte[] bytes)
        => Marshal.Copy(bytes, 0, range.Region.CpuAddress + checked((nint)range.Offset), bytes.Length);

    public ValueTask DisposeAsync()
    {
        scenes.Clear();
        sceneOrder.Clear();
        preparedNodes.Clear();
        foreach (var buffer in drawBuffers.Values)
        { buffer.Scope.Dispose(); }
        drawBuffers.Clear();
        bufferOrder.Clear();
        foreach (var image in images.Values)
        { image.Scope.Dispose(); }
        images.Clear();
        imageOrder.Clear();
        foreach (var pipeline in pipelines.Values)
        { services.Backend.DestroyRasterPipeline(pipeline); }
        pipelines.Clear();
        return ValueTask.CompletedTask;
    }
    private sealed class BuildState(NativePassBuildContext context, CancellationToken cancellation)
    {
        private int next;
        internal NativePassBuildContext Context { get; } = context;
        internal CancellationToken Cancellation { get; } = cancellation;
        internal string Name(string label) => $"{next++}/{label}";
    }
    private sealed record CachedScene(IReadOnlyList<Node> Nodes);
    private sealed record CachedBuffer(GpuResourceScope Scope, GpuBufferRef Buffer, GpuViewRef View);
    private sealed record CachedImage(GpuResourceScope Scope, GpuTextureRef Texture, NativeGpuTextureDescription Description);
    private sealed record DrawState(NativeGpuRasterPipelineHandle Pipeline, NativePassView Target, NativePassView? Backdrop,
        NativePassView? Image, NativePassView? Mask, NativePassBuffer? Data, uint DataIndex, uint Operation, float Sigma,
        float Axis, GpuShaderCodeFormat CodeFormat);
    private abstract record Node;
    private sealed record DrawNode(NativePreparedDraw Draw) : Node;
    private sealed record ClipNode(IReadOnlyList<Node> Children, NativePreparedDraw Composite) : Node;
    private sealed record LayerNode(IReadOnlyList<Node> Children, Draw2DLayerOptions Options, NativePreparedDraw Composite,
        NativePreparedDraw? Shadow, float Scale) : Node;
}
