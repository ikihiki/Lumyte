using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.TwoD;

public static class RenderGraphExtensions
{
    public static RenderPassResources AddTwoD(
        this GpuRenderGraph graph,
        string name,
        Renderer renderer,
        SceneSnapshot snapshot,
        RenderTarget target,
        bool markOutput = true)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.Update();
        target.Validate();
        GpuRenderGraphTexture resource = graph.ImportTexture(
            $"{name}-target", target.Texture, target.Description);
        return Add(
            graph.AddPass,
            graph.ImportBuffer,
            graph.ImportTexture,
            graph.CreateTexture,
            texture => { graph.MarkOutput(texture); },
            name,
            renderer,
            snapshot,
            resource,
            new(target.LoadOperation, target.StoreOperation, target.ClearColor),
            markOutput);
    }

    public static RenderPassResources AddTwoD(
        this GpuRenderGraphContributionContext context,
        string name,
        Renderer renderer,
        SceneSnapshot snapshot,
        RenderTarget target,
        bool markOutput = true)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.Update();
        target.Validate();
        GpuRenderGraphTexture resource = context.ImportTexture(
            $"{name}-target", target.Texture, target.Description);
        return Add(
            context.AddPass,
            context.ImportBuffer,
            context.ImportTexture,
            context.CreateTexture,
            texture => { context.MarkOutput(texture); },
            name,
            renderer,
            snapshot,
            resource,
            new(target.LoadOperation, target.StoreOperation, target.ClearColor),
            markOutput);
    }

    public static RenderPassResources AddTwoD(
        this GpuRenderGraph graph,
        string name,
        Renderer renderer,
        SceneSnapshot snapshot,
        GpuRenderGraphTexture target,
        RenderTargetOptions options = default,
        bool markOutput = true)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.Update();
        return Add(
            graph.AddPass,
            graph.ImportBuffer,
            graph.ImportTexture,
            graph.CreateTexture,
            texture => { graph.MarkOutput(texture); },
            name,
            renderer,
            snapshot,
            target,
            options,
            markOutput);
    }

    public static RenderPassResources AddTwoD(
        this GpuRenderGraphContributionContext context,
        string name,
        Renderer renderer,
        SceneSnapshot snapshot,
        GpuRenderGraphTexture target,
        RenderTargetOptions options = default,
        bool markOutput = true)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.Update();
        return Add(
            context.AddPass,
            context.ImportBuffer,
            context.ImportTexture,
            context.CreateTexture,
            texture => { context.MarkOutput(texture); },
            name,
            renderer,
            snapshot,
            target,
            options,
            markOutput);
    }

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
            graph.CreateTexture,
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
            context.CreateTexture,
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
        CreateTextureDelegate createTexture,
        Action<GpuRenderGraphTexture> markOutput,
        string name,
        Renderer renderer,
        IPreparedDrawing displayList,
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

        var buffers = new List<GpuRenderGraphBuffer>(4);
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
        GpuRenderGraphBuffer path = default;
        if (displayList.PathBuffer is { } pathBuffer)
        {
            path = importBuffer(
                $"{name}-paths", pathBuffer.Buffer, pathBuffer.Description);
            buffers.Add(path);
        }
        GpuRenderGraphBuffer layer = default;
        if (displayList.LayerBuffer is { } layerBuffer)
        {
            layer = importBuffer(
                $"{name}-layers", layerBuffer.Buffer, layerBuffer.Description);
            buffers.Add(layer);
        }

        var images = new GpuRenderGraphTexture[displayList.Images.Count];
        for (int index = 0; index < images.Length; index++)
        {
            PreparedImage image = displayList.Images[index];
            images[index] = importTexture(
                $"{name}-image-{index}", image.Texture, image.Description);
        }

        var resources = new PassResources(
            renderer,
            displayList,
            primitive,
            polygon,
            path,
            layer,
            images);
        int passIndex = 0;
        if (displayList.Layers.Count == 0)
        {
            AddDrawPass(addPass, name, resources, target, options, displayList.Batches, ref passIndex);
        }
        else
        {
            if (target.Description.SampleCount != 1)
            {
                throw new NotSupportedException("Isolated 2D layers require a single-sample render target.");
            }
            bool rootNeedsBackdrop = displayList.Layers.Any(layer =>
                layer.ParentId == 0
                && layer.CompositeClip is not null
                && (Renderer.RequiresBackdropSampling(layer.Options.CompositeMode)
                    || (layer.CoverageBatch.HasValue
                        && layer.Options.CompositeMode != CompositeMode.SourceOver)));
            GpuRenderGraphTexture layerTarget = target;
            RenderTargetOptions layerOptions = options;
            if (rootNeedsBackdrop
                && (target.Description.Usage & GpuTextureUsage.Sampled) == 0)
            {
                if (options.LoadOperation == GpuAttachmentLoadOperation.Load)
                {
                    throw new NotSupportedException(
                        "Backdrop-sampling composite modes require sampled target usage when preserving existing target contents.");
                }
                layerTarget = createTexture($"{name}-root-layer", LayerDescription(target.Description));
                layerOptions = options with { StoreOperation = GpuAttachmentStoreOperation.Store };
            }

            GpuRenderGraphTexture result = AddLayerContent(
                addPass,
                createTexture,
                name,
                resources,
                0,
                layerTarget,
                layerOptions,
                ref passIndex);
            if (result != target)
            {
                AddCopyPass(
                    addPass,
                    name,
                    resources,
                    result,
                    target,
                    new(
                        GpuAttachmentLoadOperation.Discard,
                        options.StoreOperation,
                        default),
                    ref passIndex);
            }
        }

        if (shouldMarkOutput) { markOutput(target); }
        return new(target, buffers, images);
    }

    private static GpuRenderGraphTexture AddLayerContent(
        AddPassDelegate addPass,
        CreateTextureDelegate createTexture,
        string name,
        PassResources resources,
        int layerId,
        GpuRenderGraphTexture target,
        RenderTargetOptions initialOptions,
        ref int passIndex)
    {
        PreparedBatch[] directBatches = resources.DisplayList.Batches
            .Where(batch => batch.LayerId == layerId)
            .ToArray();
        PreparedLayer[] children = resources.DisplayList.Layers
            .Where(layer => layer.ParentId == layerId && layer.CompositeClip is not null)
            .ToArray();
        var items = new List<LayerItem>(directBatches.Length + children.Length);
        items.AddRange(directBatches.Select(static batch => new LayerItem(batch.Sequence, batch, null)));
        items.AddRange(children.Select(static layer => new LayerItem(layer.Sequence, null, layer)));
        items.Sort(static (left, right) => left.Sequence.CompareTo(right.Sequence));

        if (items.Count == 0)
        {
            AddDrawPass(addPass, name, resources, target, initialOptions, [], ref passIndex);
            return target;
        }

        bool wroteTarget = false;
        GpuRenderGraphTexture currentTarget = target;
        int itemIndex = 0;
        while (itemIndex < items.Count)
        {
            LayerItem item = items[itemIndex];
            bool lastItem = itemIndex == items.Count - 1;
            if (item.Batch is { } firstBatch)
            {
                var group = new List<PreparedBatch> { firstBatch };
                while (itemIndex + 1 < items.Count && items[itemIndex + 1].Batch is { } nextBatch)
                {
                    group.Add(nextBatch);
                    itemIndex++;
                }
                lastItem = itemIndex == items.Count - 1;
                AddDrawPass(
                    addPass,
                    name,
                    resources,
                    currentTarget,
                    TargetOptions(
                        initialOptions,
                        wroteTarget,
                        lastItem && currentTarget == target),
                    group,
                    ref passIndex);
                wroteTarget = true;
                itemIndex++;
                continue;
            }

            PreparedLayer child = item.Layer!.Value;
            if (child.CompositeClip is not { } compositeClip)
            {
                itemIndex++;
                continue;
            }
            GpuRenderGraphTexture coverage = default;
            if (child.CoverageBatch is { } coverageBatch)
            {
                coverage = createTexture(
                    $"{name}-layer-{child.Id}-coverage",
                    LayerDescription(currentTarget.Description));
                AddDrawPass(
                    addPass,
                    name,
                    resources,
                    coverage,
                    new(
                        GpuAttachmentLoadOperation.Clear,
                        GpuAttachmentStoreOperation.Store,
                        default),
                    [coverageBatch],
                    ref passIndex);
            }
            GpuRenderGraphTexture childTarget = createTexture(
                $"{name}-layer-{child.Id}",
                LayerDescription(currentTarget.Description));
            GpuRenderGraphTexture childResult = AddLayerContent(
                addPass,
                createTexture,
                name,
                resources,
                child.Id,
                childTarget,
                new(GpuAttachmentLoadOperation.Clear, GpuAttachmentStoreOperation.Store, default),
                ref passIndex);

            if (child.MaskImageIndex >= 0)
            {
                AddMaskPass(
                    addPass,
                    name,
                    resources,
                    resources.Images[child.MaskImageIndex],
                    childResult,
                    ref passIndex);
            }

            if (child.Options.Shadow is { } shadow)
            {
                GpuRenderGraphTexture shadowSource = shadow.BlurRadius > 0
                    ? AddBlur(
                        addPass,
                        createTexture,
                        name,
                        resources,
                        childResult,
                        child.ShadowHorizontalBlurParametersOffset,
                        child.ShadowVerticalBlurParametersOffset,
                        ref passIndex)
                    : childResult;
                currentTarget = AddCompositePass(
                    addPass,
                    createTexture,
                    name,
                    resources,
                    shadowSource,
                    currentTarget,
                    TargetOptions(initialOptions, wroteTarget, false),
                    child.ShadowParametersOffset,
                    child.MaskImageIndex,
                    CompositeMode.SourceOver,
                    coverage,
                    compositeClip,
                    ref passIndex);
                wroteTarget = true;
            }

            GpuRenderGraphTexture layerSource = child.Options.BlurRadius > 0
                ? AddBlur(
                    addPass,
                    createTexture,
                    name,
                    resources,
                    childResult,
                    child.HorizontalBlurParametersOffset,
                    child.VerticalBlurParametersOffset,
                    ref passIndex)
                : childResult;
            currentTarget = AddCompositePass(
                addPass,
                createTexture,
                name,
                resources,
                layerSource,
                currentTarget,
                TargetOptions(
                    initialOptions,
                    wroteTarget,
                    lastItem && currentTarget == target),
                child.MainParametersOffset,
                child.MaskImageIndex,
                child.Options.CompositeMode,
                coverage,
                compositeClip,
                ref passIndex);
            wroteTarget = true;
            itemIndex++;
        }
        return currentTarget;
    }

    private static RenderTargetOptions TargetOptions(
        RenderTargetOptions requested,
        bool alreadyWritten,
        bool finalWrite)
        => new(
            alreadyWritten ? GpuAttachmentLoadOperation.Load : requested.LoadOperation,
            finalWrite ? requested.StoreOperation : GpuAttachmentStoreOperation.Store,
            requested.ClearColor);

    private static GpuTextureDescription LayerDescription(GpuTextureDescription target)
        => target with
        {
            Usage = GpuTextureUsage.ColorAttachment | GpuTextureUsage.Sampled,
            MipCount = 1,
            LayerCount = 1,
            SampleCount = 1,
        };

    private static GpuRenderGraphTexture AddBlur(
        AddPassDelegate addPass,
        CreateTextureDelegate createTexture,
        string name,
        PassResources resources,
        GpuRenderGraphTexture source,
        ulong horizontalParameters,
        ulong verticalParameters,
        ref int passIndex)
    {
        GpuTextureDescription description = LayerDescription(source.Description);
        GpuRenderGraphTexture horizontal = createTexture($"{name}-blur-{passIndex}-horizontal", description);
        AddFilterPass(addPass, name, resources, source, horizontal, horizontalParameters, ref passIndex);
        GpuRenderGraphTexture vertical = createTexture($"{name}-blur-{passIndex}-vertical", description);
        AddFilterPass(addPass, name, resources, horizontal, vertical, verticalParameters, ref passIndex);
        return vertical;
    }

    private static void AddDrawPass(
        AddPassDelegate addPass,
        string name,
        PassResources resources,
        GpuRenderGraphTexture target,
        RenderTargetOptions options,
        IReadOnlyList<PreparedBatch> batches,
        ref int passIndex)
    {
        string passName = passIndex == 0 ? name : $"{name}-draw-{passIndex}";
        passIndex++;
        var state = new PassState(
            PassKind.Draw,
            resources,
            target,
            options,
            batches.ToArray(),
            default,
            0,
            -1,
            CompositeMode.SourceOver,
            default);
        GpuRenderGraphPassBuilder builder = addPass(
            passName,
            state,
            static (context, value) => Record(context, value),
            GpuRenderGraphPassFlags.None);
        DeclareDrawReads(builder, resources);
        DeclareTarget(builder, target, options);
    }

    private static GpuRenderGraphTexture AddCompositePass(
        AddPassDelegate addPass,
        CreateTextureDelegate createTexture,
        string name,
        PassResources resources,
        GpuRenderGraphTexture source,
        GpuRenderGraphTexture target,
        RenderTargetOptions options,
        ulong parametersOffset,
        int maskImageIndex,
        CompositeMode blendMode,
        GpuRenderGraphTexture coverage,
        Rect compositeClip,
        ref int passIndex)
    {
        if (Renderer.RequiresBackdropSampling(blendMode)
            || (!coverage.IsNull && blendMode != CompositeMode.SourceOver))
        {
            if (options.LoadOperation != GpuAttachmentLoadOperation.Load)
            {
                AddDrawPass(
                    addPass,
                    name,
                    resources,
                    target,
                    options with { StoreOperation = GpuAttachmentStoreOperation.Store },
                    [],
                    ref passIndex);
            }

            GpuRenderGraphTexture result = createTexture(
                $"{name}-composite-{passIndex}-result",
                LayerDescription(target.Description));
            var shaderState = new PassState(
                PassKind.ShaderComposite,
                resources,
                result,
                new(
                    GpuAttachmentLoadOperation.Discard,
                    GpuAttachmentStoreOperation.Store,
                    default),
                [],
                source,
                parametersOffset,
                maskImageIndex,
                blendMode,
                target,
                coverage);
            GpuRenderGraphPassBuilder shaderBuilder = addPass(
                $"{name}-composite-{passIndex++}",
                shaderState,
                static (context, value) => Record(context, value),
                GpuRenderGraphPassFlags.None);
            shaderBuilder.Read(source, GpuStage.PixelShader, GpuBarrierHazards.Descriptors);
            shaderBuilder.Read(target, GpuStage.PixelShader, GpuBarrierHazards.Descriptors);
            if (!coverage.IsNull)
            {
                shaderBuilder.Read(coverage, GpuStage.PixelShader, GpuBarrierHazards.Descriptors);
            }
            shaderBuilder.Read(resources.LayerBuffer, GpuStage.PixelShader, GpuBarrierHazards.Descriptors);
            shaderBuilder.Write(result, GpuStage.ColorOutput);
            return result;
        }

        var state = new PassState(
            PassKind.Composite,
            resources,
            target,
            options,
            [],
            source,
            parametersOffset,
            maskImageIndex,
            blendMode,
            default,
            coverage,
            compositeClip);
        GpuRenderGraphPassBuilder builder = addPass(
            $"{name}-composite-{passIndex++}",
            state,
            static (context, value) => Record(context, value),
            GpuRenderGraphPassFlags.None);
        builder.Read(source, GpuStage.PixelShader, GpuBarrierHazards.Descriptors);
        if (!coverage.IsNull)
        {
            builder.Read(coverage, GpuStage.PixelShader, GpuBarrierHazards.Descriptors);
        }
        builder.Read(resources.LayerBuffer, GpuStage.PixelShader, GpuBarrierHazards.Descriptors);
        DeclareTarget(builder, target, options);
        return target;
    }

    private static void AddCopyPass(
        AddPassDelegate addPass,
        string name,
        PassResources resources,
        GpuRenderGraphTexture source,
        GpuRenderGraphTexture target,
        RenderTargetOptions options,
        ref int passIndex)
    {
        var state = new PassState(
            PassKind.Copy,
            resources,
            target,
            options,
            [],
            source,
            0,
            -1,
            CompositeMode.Source,
            default);
        GpuRenderGraphPassBuilder builder = addPass(
            $"{name}-copy-{passIndex++}",
            state,
            static (context, value) => Record(context, value),
            GpuRenderGraphPassFlags.None);
        builder.Read(source, GpuStage.PixelShader, GpuBarrierHazards.Descriptors);
        DeclareTarget(builder, target, options);
    }

    private static void AddMaskPass(
        AddPassDelegate addPass,
        string name,
        PassResources resources,
        GpuRenderGraphTexture mask,
        GpuRenderGraphTexture target,
        ref int passIndex)
    {
        var options = new RenderTargetOptions(
            GpuAttachmentLoadOperation.Load,
            GpuAttachmentStoreOperation.Store,
            default);
        var state = new PassState(
            PassKind.Mask,
            resources,
            target,
            options,
            [],
            mask,
            0,
            -1,
            CompositeMode.SourceOver,
            default);
        GpuRenderGraphPassBuilder builder = addPass(
            $"{name}-mask-{passIndex++}",
            state,
            static (context, value) => Record(context, value),
            GpuRenderGraphPassFlags.None);
        builder.Read(mask, GpuStage.PixelShader, GpuBarrierHazards.Descriptors);
        builder.ReadWrite(target, GpuStage.ColorOutput);
    }

    private static void AddFilterPass(
        AddPassDelegate addPass,
        string name,
        PassResources resources,
        GpuRenderGraphTexture source,
        GpuRenderGraphTexture target,
        ulong parametersOffset,
        ref int passIndex)
    {
        var options = new RenderTargetOptions(
            GpuAttachmentLoadOperation.Clear,
            GpuAttachmentStoreOperation.Store,
            default);
        var state = new PassState(
            PassKind.Blur,
            resources,
            target,
            options,
            [],
            source,
            parametersOffset,
            -1,
            CompositeMode.SourceOver,
            default);
        GpuRenderGraphPassBuilder builder = addPass(
            $"{name}-blur-{passIndex++}",
            state,
            static (context, value) => Record(context, value),
            GpuRenderGraphPassFlags.None);
        builder.Read(source, GpuStage.PixelShader, GpuBarrierHazards.Descriptors);
        builder.Read(resources.LayerBuffer, GpuStage.PixelShader, GpuBarrierHazards.Descriptors);
        builder.Write(target, GpuStage.ColorOutput);
    }

    private static void DeclareDrawReads(GpuRenderGraphPassBuilder builder, PassResources resources)
    {
        if (!resources.PrimitiveBuffer.IsNull)
        {
            builder.Read(resources.PrimitiveBuffer, GpuStage.VertexShader, GpuBarrierHazards.Descriptors);
        }
        if (!resources.PolygonBuffer.IsNull)
        {
            builder.Read(resources.PolygonBuffer, GpuStage.VertexShader, GpuBarrierHazards.Descriptors);
        }
        if (!resources.PathBuffer.IsNull)
        {
            builder.Read(
                resources.PathBuffer,
                GpuStage.VertexShader | GpuStage.PixelShader,
                GpuBarrierHazards.Descriptors);
        }
        foreach (GpuRenderGraphTexture image in resources.Images)
        {
            builder.Read(image, GpuStage.PixelShader, GpuBarrierHazards.Descriptors);
        }
    }

    private static void DeclareTarget(
        GpuRenderGraphPassBuilder builder,
        GpuRenderGraphTexture target,
        RenderTargetOptions options)
    {
        if (options.LoadOperation == GpuAttachmentLoadOperation.Load)
        {
            builder.ReadWrite(target, GpuStage.ColorOutput);
        }
        else
        {
            builder.Write(target, GpuStage.ColorOutput);
        }
    }

    private static void Record(GpuRenderGraphPassContextView context, PassState state)
    {
        state.Resources.DisplayList.VerifyAlive();
        GpuTextureDescription target = state.Target.Description;
        GpuCommandBuffer commands = context.Commands.BeginRendering([
            new(
                context.GetTextureView(state.Target),
                state.Options.LoadOperation,
                state.Options.StoreOperation,
                state.Options.ClearColor),
        ]);

        if (state.Kind == PassKind.Draw)
        {
            RecordDraw(context, commands, state, target);
        }
        else if (state.Kind == PassKind.Copy)
        {
            var table = new GpuResourceTable(1, 1);
            table.SetTexture(0, context.GetTextureView(state.Source).Id);
            table.SetSampler(0, state.Resources.Renderer.GetLayerSampler());
            commands
                .SetPipeline(state.Resources.Renderer.GetCopyPipeline(target.Format))
                .SetResourceTable(table)
                .SetViewportAndScissor(
                    new(0, 0, target.Width, target.Height),
                    new(0, 0, target.Width, target.Height))
                .Draw(6);
        }
        else if (state.Kind == PassKind.Mask)
        {
            var table = new GpuResourceTable(1, 1);
            table.SetTexture(0, context.GetTextureView(state.Source).Id);
            table.SetSampler(0, state.Resources.Renderer.GetLayerSampler());
            commands
                .SetPipeline(state.Resources.Renderer.GetMaskPipeline(target.Format))
                .SetResourceTable(table)
                .SetViewportAndScissor(
                    new(0, 0, target.Width, target.Height),
                    new(0, 0, target.Width, target.Height))
                .Draw(6);
        }
        else
        {
            GpuBufferView parameters = context.GetBufferView(
                state.Resources.LayerBuffer,
                new(state.ParametersOffset, GpuLayerCommand.Size));
            GpuResourceTable table;
            GpuRasterPipelineHandle pipeline;
            if (state.Kind == PassKind.Blur)
            {
                table = new(1, 1, 1);
                table.SetTexture(0, context.GetTextureView(state.Source).Id);
                table.SetSampler(0, state.Resources.Renderer.GetLayerSampler());
                pipeline = state.Resources.Renderer.GetBlurPipeline(target.Format);
            }
            else if (state.Kind == PassKind.ShaderComposite)
            {
                table = new(3, 1, 1);
                table.SetTexture(0, context.GetTextureView(state.Source).Id);
                table.SetTexture(1, context.GetTextureView(state.Backdrop).Id);
                table.SetTexture(
                    2,
                    context.GetTextureView(
                        state.Coverage.IsNull ? state.Source : state.Coverage).Id);
                table.SetSampler(0, state.Resources.Renderer.GetLayerSampler());
                pipeline = state.Resources.Renderer.GetCompositePipeline(target.Format);
            }
            else
            {
                TextureId source = context.GetTextureView(state.Source).Id;
                if (state.Coverage.IsNull)
                {
                    table = new(1, 1, 1);
                    table.SetTexture(0, source);
                    pipeline = state.Resources.Renderer.GetLayerPipeline(
                        target.Format,
                        state.BlendMode);
                }
                else
                {
                    table = new(3, 1, 1);
                    table.SetTexture(0, source);
                    table.SetTexture(1, source);
                    table.SetTexture(2, context.GetTextureView(state.Coverage).Id);
                    pipeline = state.Resources.Renderer.GetClippedLayerPipeline(target.Format);
                }
                table.SetSampler(0, state.Resources.Renderer.GetLayerSampler());
            }
            table.SetBuffer(0, parameters.Id);
            GpuScissorRect scissor = state.Kind == PassKind.Composite
                && state.CompositeClip is { } compositeClip
                    ? ToScissor(compositeClip, target)
                    : new(0, 0, target.Width, target.Height);
            commands
                .SetPipeline(pipeline)
                .SetResourceTable(table)
                .SetViewportAndScissor(
                    new(0, 0, target.Width, target.Height),
                    scissor)
                .Draw(6);
        }

        commands.EndRendering();
    }

    private static void RecordDraw(
        GpuRenderGraphPassContextView context,
        GpuCommandBuffer commands,
        PassState state,
        GpuTextureDescription target)
    {
        foreach (PreparedBatch batch in state.Batches)
        {
            GpuRenderGraphBuffer resource = batch.Kind switch
            {
                PreparedBatchKind.Polygon => state.Resources.PolygonBuffer,
                PreparedBatchKind.Path => state.Resources.PathBuffer,
                _ => state.Resources.PrimitiveBuffer,
            };
            GpuBufferView view = context.GetBufferView(resource, new(batch.BufferOffset, batch.BufferLength));
            GpuResourceTable table = batch.Kind is PreparedBatchKind.Image or PreparedBatchKind.DistanceField
                ? ImageTable(context, state.Resources, batch, view)
                : new(0, 0, 1);
            table.SetBuffer(0, view.Id);

            commands
                .SetPipeline(state.Resources.Renderer.GetPipeline(target.Format, batch.Kind))
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
    }

    private static GpuResourceTable ImageTable(
        GpuRenderGraphPassContextView context,
        PassResources resources,
        PreparedBatch batch,
        GpuBufferView view)
    {
        PreparedImage image = resources.DisplayList.Images[batch.ImageIndex];
        var table = new GpuResourceTable(1, 1, 1);
        table.SetTexture(0, context.GetTextureView(resources.Images[batch.ImageIndex]).Id);
        table.SetSampler(0, image.Sampler);
        table.SetBuffer(0, view.Id);
        return table;
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

    private delegate GpuRenderGraphTexture CreateTextureDelegate(
        string name,
        GpuTextureDescription description);

    private readonly record struct PassState(
        PassKind Kind,
        PassResources Resources,
        GpuRenderGraphTexture Target,
        RenderTargetOptions Options,
        PreparedBatch[] Batches,
        GpuRenderGraphTexture Source,
        ulong ParametersOffset,
        int MaskImageIndex,
        CompositeMode BlendMode,
        GpuRenderGraphTexture Backdrop,
        GpuRenderGraphTexture Coverage = default,
        Rect? CompositeClip = null);

    private readonly record struct PassResources(
        Renderer Renderer,
        IPreparedDrawing DisplayList,
        GpuRenderGraphBuffer PrimitiveBuffer,
        GpuRenderGraphBuffer PolygonBuffer,
        GpuRenderGraphBuffer PathBuffer,
        GpuRenderGraphBuffer LayerBuffer,
        GpuRenderGraphTexture[] Images);

    private readonly record struct LayerItem(
        int Sequence,
        PreparedBatch? Batch,
        PreparedLayer? Layer);

    private enum PassKind
    {
        Draw,
        Composite,
        ShaderComposite,
        Copy,
        Blur,
        Mask,
    }
}
