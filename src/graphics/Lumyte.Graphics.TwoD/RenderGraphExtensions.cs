using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.TwoD;

public static class RenderGraphExtensions
{
    public static RenderPassResources AddTwoD(
        this GpuRenderGraph graph,
        string name,
        Renderer renderer,
        PreparedDisplayList displayList,
        RenderTarget target,
        bool markOutput = true)
    {
        ArgumentNullException.ThrowIfNull(graph);
        target.Validate();
        GpuRenderGraphTexture resource = graph.ImportTexture(
            $"{name}-target", target.Texture, target.Description);
        return graph.AddTwoD(
            name,
            renderer,
            displayList,
            resource,
            new(target.LoadOperation, target.StoreOperation, target.ClearColor),
            markOutput);
    }

    public static RenderPassResources AddTwoD(
        this GpuRenderGraph graph,
        string name,
        Renderer renderer,
        PreparedDisplayList displayList,
        GpuRenderGraphTexture target,
        RenderTargetOptions options = default,
        bool markOutput = true)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return Add(
            graph.AddPass,
            graph.ImportBuffer,
            graph.ImportTexture,
            texture => { graph.MarkOutput(texture); },
            name,
            renderer,
            displayList,
            target,
            options,
            markOutput);
    }

    public static RenderPassResources AddTwoD(
        this GpuRenderGraphContributionContext context,
        string name,
        Renderer renderer,
        PreparedDisplayList displayList,
        RenderTarget target,
        bool markOutput = true)
    {
        ArgumentNullException.ThrowIfNull(context);
        target.Validate();
        GpuRenderGraphTexture resource = context.ImportTexture(
            $"{name}-target", target.Texture, target.Description);
        return context.AddTwoD(
            name,
            renderer,
            displayList,
            resource,
            new(target.LoadOperation, target.StoreOperation, target.ClearColor),
            markOutput);
    }

    public static RenderPassResources AddTwoD(
        this GpuRenderGraphContributionContext context,
        string name,
        Renderer renderer,
        PreparedDisplayList displayList,
        GpuRenderGraphTexture target,
        RenderTargetOptions options = default,
        bool markOutput = true)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Add(
            context.AddPass,
            context.ImportBuffer,
            context.ImportTexture,
            texture => { context.MarkOutput(texture); },
            name,
            renderer,
            displayList,
            target,
            options,
            markOutput);
    }

    private static RenderPassResources Add(
        AddPassDelegate addPass,
        ImportBufferDelegate importBuffer,
        ImportTextureDelegate importTexture,
        Action<GpuRenderGraphTexture> markOutput,
        string name,
        Renderer renderer,
        PreparedDisplayList displayList,
        GpuRenderGraphTexture target,
        RenderTargetOptions options,
        bool shouldMarkOutput)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(displayList);
        displayList.VerifyAlive();
        options.Validate();
        if (!ReferenceEquals(displayList.Owner, renderer))
        {
            throw new ArgumentException("Prepared display list belongs to another renderer.", nameof(displayList));
        }
        if (displayList.TargetDescription != target.Description)
        {
            throw new ArgumentException(
                "Prepared display list and render target descriptions must match.",
                nameof(target));
        }

        var buffers = new List<GpuRenderGraphBuffer>(2);
        GpuRenderGraphBuffer primitive = default;
        if (displayList.PrimitiveBuffer is { } primitiveBuffer)
        {
            primitive = importBuffer(
                $"{name}-commands", primitiveBuffer.Buffer, primitiveBuffer.Description);
            buffers.Add(primitive);
        }
        GpuRenderGraphBuffer polygon = default;
        if (displayList.PolygonBuffer is { } polygonBuffer)
        {
            polygon = importBuffer(
                $"{name}-polygons", polygonBuffer.Buffer, polygonBuffer.Description);
            buffers.Add(polygon);
        }

        var images = new GpuRenderGraphTexture[displayList.Images.Count];
        for (int index = 0; index < images.Length; index++)
        {
            PreparedImage image = displayList.Images[index];
            images[index] = importTexture(
                $"{name}-image-{index}", image.Texture, image.Description);
        }

        var state = new PassState(
            renderer,
            displayList,
            target,
            options,
            primitive,
            polygon,
            images);
        GpuRenderGraphPassBuilder builder = addPass(
            name,
            state,
            static (context, value) => Record(context, value),
            GpuRenderGraphPassFlags.None);
        if (!primitive.IsNull)
        {
            builder.Read(primitive, GpuStage.VertexShader, GpuBarrierHazards.Descriptors);
        }
        if (!polygon.IsNull)
        {
            builder.Read(polygon, GpuStage.VertexShader, GpuBarrierHazards.Descriptors);
        }
        foreach (GpuRenderGraphTexture image in images)
        {
            builder.Read(image, GpuStage.PixelShader, GpuBarrierHazards.Descriptors);
        }
        if (options.LoadOperation == GpuAttachmentLoadOperation.Load)
        {
            builder.ReadWrite(target, GpuStage.ColorOutput);
        }
        else
        {
            builder.Write(target, GpuStage.ColorOutput);
        }

        if (shouldMarkOutput) { markOutput(target); }
        return new(target, buffers, images);
    }

    private static void Record(GpuRenderGraphPassContextView context, PassState state)
    {
        state.DisplayList.VerifyAlive();
        GpuTextureDescription target = state.DisplayList.TargetDescription;
        GpuCommandBuffer commands = context.Commands.BeginRendering([
            new(
                context.GetTextureView(state.Target),
                state.Options.LoadOperation,
                state.Options.StoreOperation,
                state.Options.ClearColor),
        ]);

        foreach (PreparedBatch batch in state.DisplayList.Batches)
        {
            GpuRenderGraphBuffer resource = batch.Kind == PreparedBatchKind.Polygon
                ? state.PolygonBuffer
                : state.PrimitiveBuffer;
            GpuBufferView view = context.GetBufferView(
                resource,
                new(batch.BufferOffset, batch.BufferLength));
            GpuResourceTable table;
            if (batch.Kind == PreparedBatchKind.Image)
            {
                PreparedImage image = state.DisplayList.Images[batch.ImageIndex];
                table = new(1, 1, 1);
                table.SetTexture(0, context.GetTextureView(state.Images[batch.ImageIndex]).Id);
                table.SetSampler(0, image.Sampler);
            }
            else
            {
                table = new(0, 0, 1);
            }
            table.SetBuffer(0, view.Id);

            commands
                .SetPipeline(state.Renderer.GetPipeline(target.Format, batch.Kind))
                .SetResourceTable(table)
                .SetViewportAndScissor(
                    new(0, 0, target.Width, target.Height),
                    ToScissor(batch.Clip, target));
            if (batch.Kind == PreparedBatchKind.Polygon)
            {
                commands.Draw(batch.DrawCount);
            }
            else
            {
                commands.Draw(6, batch.DrawCount);
            }
        }

        commands.EndRendering();
    }

    private static GpuScissorRect ToScissor(Rect clip, GpuTextureDescription target)
    {
        uint left = checked((uint)Math.Clamp(MathF.Floor(clip.X), 0, (float)target.Width));
        uint top = checked((uint)Math.Clamp(MathF.Floor(clip.Y), 0, (float)target.Height));
        uint right = checked((uint)Math.Clamp(MathF.Ceiling(clip.Right), 0, (float)target.Width));
        uint bottom = checked((uint)Math.Clamp(MathF.Ceiling(clip.Bottom), 0, (float)target.Height));
        return new(left, top, right - left, bottom - top);
    }

    private delegate GpuRenderGraphPassBuilder AddPassDelegate(
        string name,
        PassState state,
        GpuRenderGraphPassAction<PassState> record,
        GpuRenderGraphPassFlags flags);

    private delegate GpuRenderGraphBuffer ImportBufferDelegate(
        string name,
        GpuBufferHandle buffer,
        GpuBufferDescription description);

    private delegate GpuRenderGraphTexture ImportTextureDelegate(
        string name,
        GpuTextureHandle texture,
        GpuTextureDescription description);

    private readonly record struct PassState(
        Renderer Renderer,
        PreparedDisplayList DisplayList,
        GpuRenderGraphTexture Target,
        RenderTargetOptions Options,
        GpuRenderGraphBuffer PrimitiveBuffer,
        GpuRenderGraphBuffer PolygonBuffer,
        GpuRenderGraphTexture[] Images);
}
