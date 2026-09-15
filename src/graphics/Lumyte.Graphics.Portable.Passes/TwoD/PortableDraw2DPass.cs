using System.Numerics;
using System.Runtime.InteropServices;

using Lumyte.Graphics.Passes;
using Lumyte.Graphics.Portable.RenderGraph;
using Lumyte.Graphics.Portable.Resources;
using Lumyte.Graphics.Portable.Shaders;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Portable.Passes;

public static class Portable2DPasses
{
    /// <summary>Registers the Portable implementation without creating a GPU object.</summary>
    public static PortableRenderPassRegistry Add2DRendering(this PortableRenderPassRegistry registry)
        => Add2DRendering(registry, Portable2DShaders.CreatePackage());

    /// <summary>Registers a prepared shader package; no file loading is performed by a pass.</summary>
    public static PortableRenderPassRegistry Add2DRendering(this PortableRenderPassRegistry registry, PortableShaderPackage shaders)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(shaders);
        registry.Register(Draw2DPassContract.Instance, services => new PortableDraw2DPass(services, shaders));
        return registry;
    }
}

public static class Portable2DShaders
{
    /// <summary>Returns the immutable WGSL shipped with this implementation, ready for host registration.</summary>
    public static PortableShaderPackage CreatePackage()
    {
        using Stream stream = typeof(Portable2DShaders).Assembly.GetManifestResourceStream("Lumyte.Graphics.Portable.Passes.TwoD.Draw2D.wgsl")
            ?? throw new InvalidOperationException("The embedded Portable 2D shader is missing.");
        using var reader = new StreamReader(stream);
        return new(1, reader.ReadToEnd(), [new(GpuShaderStage.Vertex, "vertex"), new(GpuShaderStage.Pixel, "fragment")],
            PortableShaderFeatures.ImmediateAddressSpace,
            [new([new(0,GpuShaderStage.Pixel,new GpuBufferBindingLayout(GpuBufferBindingType.ReadOnlyStorage)),
                new(1,GpuShaderStage.Pixel,new GpuTextureBindingLayout(GpuTextureSampleType.UnfilterableFloat)),
                new(2,GpuShaderStage.Pixel,new GpuTextureBindingLayout(GpuTextureSampleType.UnfilterableFloat)),
                new(3,GpuShaderStage.Pixel,new GpuTextureBindingLayout(GpuTextureSampleType.UnfilterableFloat))])],
            new("Root", 16, 4, [new("offset", "u32", 0, 4, 4), new("reserved0", "u32", 4, 4, 4), new("reserved1", "u32", 8, 4, 4), new("reserved2", "u32", 12, 4, 4)]),
            [], new([]), "Lumyte.Portable.Draw2D.v1");
    }
}

internal sealed class PortableDraw2DPass(PortablePassServices services, PortableShaderPackage shaders)
    : IPortableRenderPass<Draw2DPassRequest, Draw2DPassResult>
{
    private readonly Dictionary<GpuFormat, GpuRasterPipelineHandle> pipelines = [];
    private readonly Dictionary<SceneKey, PreparedScene> scenes = [];
    private readonly Dictionary<GpuUploadDataKey, PortablePassContentGeneration<GpuTextureRef>> images = [];
    private PortableShaderProgram? program;
    private int sequence;
    public async ValueTask BuildAsync(PortablePassBuildContext context, Draw2DPassRequest request, Draw2DPassResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        sequence = 0;
        Draw2DScene scene = context.GetInput(request.Scene);
        PortablePassTexture target = context.ImportTexture(request.Color);
        if (scene.Commands.Count == 0)
        {
            PortablePassView view = context.CreateView(Name("empty"), target);
            context.AddPass(Name("empty"), view, static (record, v) => { record.Commands.BeginRendering([new(record.GetTextureView(v), GpuAttachmentLoadOperation.Load)]); record.Commands.EndRendering(); })
                .ReadWrite(target, PortablePassUsage.ColorAttachment);
            return;
        }
        PortablePassTexture initial = CreateTarget(context, target.Description, "backdrop");
        Copy(context, target, initial);
        PortablePassTexture final = initial;
        foreach (SceneKey key in Chunks(scene, Matrix3x2.CreateScale(scene.DeviceScale), null, scene.DeviceScale))
        {
            PreparedScene prepared = GetPrepared(key, scene.DeviceScale);
            PortablePassBuffer data = await PrepareBufferAsync(context, prepared, cancellationToken);
            final = await RenderAsync(context, prepared.Plan.Nodes, final, data, cancellationToken);
        }
        Copy(context, final, target);
    }
    private PreparedScene GetPrepared(SceneKey key, float scale)
    {
        if (!scenes.TryGetValue(key, out PreparedScene? prepared))
        {
            var clips = new List<Vector4[]>();
            for (ClipChain? clip = key.Clips; clip is not null; clip = clip.Previous)
            {
                Matrix3x2 transform = clip.Clip.Transform * clip.Parent;
                var contours = clip.Clip.Path is { } path ? PortableDrawingGeometry.Flatten(path, PortableDrawingGeometry.Scale(transform)) : PortableDrawingGeometry.Rectangle(clip.Clip.Rectangle!.Value);
                clips.Add([new((float)clip.Clip.FillRule, 0, 0, 0), .. PortableDrawingGeometry.Edges(contours, transform)]);
            }
            prepared = new(PortableDrawingPlan.Compile(key.Scene, key.Transform, scale, clips));
            scenes.Add(key, prepared);
            if (scenes.Count > 4096)
            { SceneKey oldest = scenes.Keys.First(); scenes[oldest].Generation?.Dispose(); scenes.Remove(oldest); }
        }
        return prepared;
    }
    private static IEnumerable<SceneKey> Chunks(Draw2DScene scene, Matrix3x2 transform, ClipChain? clips, float deviceScale)
    {
        // Retained store pages contain only shared child scenes. Unchanged content reuses its
        // own GPU packet even when a sibling or an ancestor page has a new CPU identity.
        if (scene.Commands.Count > 0 && scene.Commands.All(c => c is Draw2DSceneCommand))
        {
            foreach (Draw2DSceneCommand child in scene.Commands.Cast<Draw2DSceneCommand>())
            {
                ClipChain? chain = clips;
                foreach (Draw2DClip clip in child.State.Clips)
                { chain = new(chain, clip, transform); }
                foreach (SceneKey chunk in Chunks(child.Content, child.State.Transform * transform, chain, deviceScale))
                { yield return chunk; }
            }
        }
        else
        { yield return new(scene, transform, clips, deviceScale); }
    }
    private async ValueTask<PortablePassBuffer> PrepareBufferAsync(PortablePassBuildContext context, PreparedScene prepared, CancellationToken cancellationToken)
    {
        if (!context.TryUseContent(prepared.Generation, out GpuBufferRef buffer))
        {
            prepared.Generation?.Dispose();
            byte[] bytes = MemoryMarshal.AsBytes(prepared.Plan.Data.AsSpan()).ToArray();
            using GpuResourceScope scope = services.Resources.CreateScope();
            buffer = scope.CreateBuffer(new((ulong)bytes.Length, GpuBufferUsage.Storage | GpuBufferUsage.CopyDestination));
            PortablePassBuffer destination = context.ImportBuffer(buffer);
            PortablePassBuffer staging = await StageAsync(context, bytes, cancellationToken);
            PortablePassBuilder copy = context.AddPass(Name("scene upload"), (staging, destination, (ulong)bytes.Length), static (record, s) =>
                record.Commands.CopyBuffer(record.GetBufferRange(s.staging, 0, s.Item3), record.GetBufferRange(s.destination, 0, s.Item3)))
                .Read(staging, PortablePassUsage.CopySource).Write(destination, PortablePassUsage.CopyDestination);
            prepared.Generation = context.RegisterContent(buffer, services.Resources.Pin(buffer), [copy]);
            _ = context.TryUseContent(prepared.Generation, out buffer);
        }
        return context.ImportBuffer(buffer);
    }
    private async ValueTask<PortablePassTexture> RenderAsync(PortablePassBuildContext context, IReadOnlyList<PortableDrawingNode> nodes,
        PortablePassTexture initial, PortablePassBuffer data, CancellationToken cancellationToken)
    {
        PortablePassTexture current = initial;
        foreach (PortableDrawingNode node in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PortablePassTexture source = current;
            PortablePassTexture mask = current;
            if (node.Children is { } children)
            {
                var layerDescription = node.PreserveBackdrop ? current.Description : initial.Description with { Format = GpuFormat.Rgba16Float };
                PortablePassTexture layer = CreateTarget(context, layerDescription, "layer");
                if (node.PreserveBackdrop)
                { Copy(context, current, layer); }
                else
                { Clear(context, layer); }
                source = await RenderAsync(context, children, layer, data, cancellationToken);
                PortablePassTexture originalSource = source;
                if (node.BlurHorizontal is { } horizontal && node.BlurVertical is { } vertical)
                { source = Draw(context, horizontal, source, source, source, data); source = Draw(context, vertical, source, source, source, data); }
                if (node.Mask is { } maskNode)
                {
                    PortablePassTexture emptyMask = CreateTarget(context, initial.Description with { Format = GpuFormat.Rgba16Float }, "mask");
                    Clear(context, emptyMask);
                    mask = await RenderAsync(context, [maskNode], emptyMask, data, cancellationToken);
                }
                if (node.Shadow is { } shadow)
                {
                    PortablePassTexture shadowSource = originalSource;
                    if (node.ShadowHorizontal is { } sh && node.ShadowVertical is { } sv)
                    { shadowSource = Draw(context, sh, shadowSource, shadowSource, shadowSource, data); shadowSource = Draw(context, sv, shadowSource, shadowSource, shadowSource, data); }
                    current = Draw(context, shadow, current, shadowSource, mask, data);
                }
            }
            else if (node.Image is { } image)
            { source = await ImageAsync(context, image, cancellationToken); }
            if (node.DistanceField is { } distanceField)
            { mask = await ImageAsync(context, distanceField, cancellationToken); }
            current = Draw(context, node.Header, current, source, mask, data);
        }
        return current;
    }
    private PortablePassTexture Draw(PortablePassBuildContext context, uint header, PortablePassTexture backdrop,
        PortablePassTexture source, PortablePassTexture mask, PortablePassBuffer data)
    {
        PortablePassTexture output = CreateTarget(context, backdrop.Description, "draw");
        PortablePassView target = context.CreateView(Name("attachment"), output), backdropView = context.CreateView(Name("backdrop"), backdrop),
            sourceView = context.CreateView(Name("image"), source), maskView = context.CreateView(Name("mask"), mask);
        GpuRasterPipelineHandle pipeline = Pipeline(output.Description.Format);
        PortablePassBindings bindings = context.CreateBindings(Name("inputs"), program!, 0, new Inputs(data, backdropView, sourceView, maskView));
        var state = new DrawState(target, bindings, pipeline, new Root(header, 0, 0, 0), output.Description.Width, output.Description.Height);
        var pass = context.AddPass(Name("2D"), state, static (record, s) =>
        {
            record.Commands.BeginRendering([new(record.GetTextureView(s.Target), GpuAttachmentLoadOperation.Clear)]);
            record.Commands.SetPipeline(s.Pipeline);
            record.Commands.SetBindings(0, record.GetBindings(s.Bindings));
            Root root = s.Root;
            record.Commands.SetRootData(in root);
            record.Commands.SetViewportAndScissor(new(0, 0, s.Width, s.Height), new(0, 0, s.Width, s.Height));
            record.Commands.Draw(3);
            record.Commands.EndRendering();
        }).Read(data, PortablePassUsage.StorageRead).Read(backdrop, PortablePassUsage.SampledRead).Write(output, PortablePassUsage.ColorAttachment);
        if (!ReferenceEquals(source, backdrop))
        { pass.Read(source, PortablePassUsage.SampledRead); }
        if (!ReferenceEquals(mask, backdrop) && !ReferenceEquals(mask, source))
        { pass.Read(mask, PortablePassUsage.SampledRead); }
        return output;
    }
    private async ValueTask<PortablePassTexture> ImageAsync(PortablePassBuildContext context, Draw2DImageSource source, CancellationToken cancellationToken)
    {
        if (source.Texture is { } logical)
        { return context.ImportTexture(logical); }
        GpuImageUploadData upload = source.Upload!;
        images.TryGetValue(upload.Key, out var generation);
        if (context.TryUseContent(generation, out GpuTextureRef reference))
        { return context.ImportTexture(reference); }
        generation?.Dispose();
        var d = upload.Description;
        GpuFormat format = d.Format switch { GpuFormat.Rgba8UnormSrgb => GpuFormat.Rgba8Unorm, GpuFormat.Bgra8UnormSrgb => GpuFormat.Bgra8Unorm, _ => d.Format };
        using GpuResourceScope scope = services.Resources.CreateScope();
        reference = scope.CreateTexture(new(GpuTextureDimension.Texture2D, d.Width, d.Height, 1, 1, 1, 1, format, GpuTextureUsage.Sampled | GpuTextureUsage.CopyDestination));
        PortablePassTexture texture = context.ImportTexture(reference);
        GpuImageSubresourceData subresource = upload.Subresources.First(s => s.MipLevel == 0 && s.ArrayLayer == 0);
        int pixelBytes = format switch { GpuFormat.R8Unorm => 1, GpuFormat.Rg8Unorm => 2, GpuFormat.Rgba16Float => 8, _ => 4 };
        int rowBytes = checked((int)d.Width * pixelBytes), pitch = checked((rowBytes + 255) & ~255), height = checked((int)d.Height);
        byte[] bytes = new byte[checked(pitch * height)];
        int sourcePitch = checked((int)(subresource.RowStride == 0 ? (ulong)rowBytes : subresource.RowStride));
        for (int y = 0; y < height; y++)
        { subresource.Data.Span.Slice(checked(y * sourcePitch), rowBytes).CopyTo(bytes.AsSpan(y * pitch, rowBytes)); }
        PortablePassBuffer staging = await StageAsync(context, bytes, cancellationToken);
        var footprint = new GpuTextureCopyFootprint(0, GpuTextureAspect.All, default, new(d.Width, d.Height, 1), (ulong)pitch, (ulong)bytes.Length);
        PortablePassBuilder writer = context.AddPass(Name("image upload"), (staging, texture, footprint, Length: (ulong)bytes.Length), static (record, s) =>
            record.Commands.CopyBufferToTexture(record.GetBufferRange(s.staging, 0, s.Length), record.GetTexture(s.texture), s.footprint))
            .Read(staging, PortablePassUsage.CopySource).Write(texture, PortablePassUsage.CopyDestination);
        generation = context.RegisterContent(reference, services.Resources.Pin(reference), [writer]);
        images[upload.Key] = generation;
        if (images.Count > 1024)
        { GpuUploadDataKey oldest = images.Keys.First(); images[oldest].Dispose(); images.Remove(oldest); }
        _ = context.TryUseContent(generation, out reference);
        return texture;
    }
    private async ValueTask<PortablePassBuffer> StageAsync(PortablePassBuildContext context, byte[] data, CancellationToken cancellationToken)
    {
        using GpuResourceScope scope = services.Resources.CreateScope();
        GpuBufferRef staging = scope.CreateBuffer(new((ulong)data.Length, GpuBufferUsage.MapWrite | GpuBufferUsage.CopySource));
        using (GpuMappedBufferRange mapped = await services.Resources.MapBufferAsync(staging, GpuMapMode.Write, cancellationToken: cancellationToken))
        { data.CopyTo(mapped.Memory.Span); }
        return context.ImportBuffer(staging);
    }
    private PortablePassTexture CreateTarget(PortablePassBuildContext context, GpuTextureDescription description, string name)
        => context.CreateTexture(Name(name), description with { Usage = GpuTextureUsage.Sampled | GpuTextureUsage.ColorAttachment | GpuTextureUsage.CopySource | GpuTextureUsage.CopyDestination });
    private void Clear(PortablePassBuildContext context, PortablePassTexture texture)
    {
        var view = context.CreateView(Name("clear"), texture);
        context.AddPass(Name("clear"), view, static (record, v) => { record.Commands.BeginRendering([new(record.GetTextureView(v), GpuAttachmentLoadOperation.Clear)]); record.Commands.EndRendering(); })
            .Write(texture, PortablePassUsage.ColorAttachment);
    }
    private void Copy(PortablePassBuildContext context, PortablePassTexture source, PortablePassTexture target)
    {
        var footprint = new GpuTextureCopyFootprint(0, GpuTextureAspect.All, default, new(target.Description.Width, target.Description.Height, 1));
        context.AddPass(Name("copy"), (source, target, footprint), static (record, s) => record.Commands.CopyTexture(record.GetTexture(s.source), s.footprint, record.GetTexture(s.target), s.footprint))
            .Read(source, PortablePassUsage.CopySource).Write(target, PortablePassUsage.CopyDestination);
    }
    private GpuRasterPipelineHandle Pipeline(GpuFormat format)
    {
        if (pipelines.TryGetValue(format, out var pipeline))
        { return pipeline; }
        program ??= services.ShaderLoader.Load(shaders);
        pipeline = services.Backend.CreateRasterPipeline(new([new(format)]), program.Description);
        pipelines.Add(format, pipeline);
        return pipeline;
    }
    private string Name(string prefix) => $"{prefix} {sequence++}";
    public ValueTask DisposeAsync()
    {
        foreach (var scene in scenes.Values)
        { scene.Generation?.Dispose(); }
        scenes.Clear();
        foreach (var generation in images.Values)
        { generation.Dispose(); }
        images.Clear();
        foreach (var pipeline in pipelines.Values)
        { services.Backend.DestroyRasterPipeline(pipeline); }
        pipelines.Clear();
        program?.Dispose();
        program = null;
        return ValueTask.CompletedTask;
    }
    [StructLayout(LayoutKind.Sequential)] private readonly record struct Root(uint Offset, uint Reserved0, uint Reserved1, uint Reserved2);
    private sealed record DrawState(PortablePassView Target, PortablePassBindings Bindings, GpuRasterPipelineHandle Pipeline, Root Root, uint Width, uint Height);
    private sealed class PreparedScene(PortableDrawingPlan plan)
    { internal PortableDrawingPlan Plan { get; } = plan; internal PortablePassContentGeneration<GpuBufferRef>? Generation { get; set; } }
    private sealed record ClipChain(ClipChain? Previous, Draw2DClip Clip, Matrix3x2 Parent);
    private sealed record SceneKey(Draw2DScene Scene, Matrix3x2 Transform, ClipChain? Clips, float DeviceScale);
    private sealed class Inputs(PortablePassBuffer data, PortablePassView backdrop, PortablePassView image, PortablePassView mask) : IPortablePassBindingInputs
    { public void Write(PortablePassBindingWriter writer) { writer.Buffer(0, data); writer.Texture(1, backdrop); writer.Texture(2, image); writer.Texture(3, mask); } }
}
