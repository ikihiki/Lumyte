using System.Numerics;
using System.Runtime.InteropServices;

using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.RenderGraph.Common;

/// <summary>Bufferless draw extensions for render-graph building surfaces.</summary>
public static class DrawRenderGraphExtensions
{
    public static DrawRenderPassResources AddDraw(
        this GpuRenderGraph graph,
        string name,
        DrawData draw,
        DrawRenderTarget target,
        bool markOutput = true)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        draw.Validate();
        target.Validate();

        GpuRenderGraphTexture targetResource = graph.ImportTexture(
            $"{name}-target", target.View.Texture, target.Description);
        return graph.AddDraw(name, draw, target, targetResource, markOutput);
    }

    /// <summary>Adds a draw to a target already declared by another graph-building component.</summary>
    public static DrawRenderPassResources AddDraw(
        this GpuRenderGraph graph,
        string name,
        DrawData draw,
        DrawRenderTarget target,
        GpuRenderGraphTexture targetResource,
        bool markOutput = true)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        draw.Validate();
        target.Validate();
        RequireMatchingTarget(target, targetResource);

        GpuRenderGraphTexture[] sampledResources = ImportSampledTextures(
            draw.Material,
            (localName, texture) => graph.ImportTexture(localName, texture.Texture, texture.Description),
            name);
        GpuRenderGraphBuffer[] bufferResources = ImportShaderBuffers(
            draw.Material,
            (localName, buffer) => graph.ImportBuffer(localName, buffer.Buffer, buffer.Description),
            name);
        GpuRenderGraphShaderBindings? bindings = CreateShaderBindings(
            draw.Material, sampledResources, bufferResources);
        AddPass(graph.AddPass, name, draw, target, targetResource, bindings);
        if (markOutput) { graph.MarkOutput(targetResource); }
        return new(targetResource, sampledResources, bufferResources);
    }

    public static DrawRenderPassResources AddDraw(
        this GpuRenderGraphContributionContext context,
        string name,
        DrawData draw,
        DrawRenderTarget target,
        bool markOutput = true)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        draw.Validate();
        target.Validate();

        GpuRenderGraphTexture targetResource = context.ImportTexture(
            $"{name}-target", target.View.Texture, target.Description);
        return context.AddDraw(name, draw, target, targetResource, markOutput);
    }

    /// <summary>Adds a draw to a target published by an earlier frame contributor.</summary>
    public static DrawRenderPassResources AddDraw(
        this GpuRenderGraphContributionContext context,
        string name,
        DrawData draw,
        DrawRenderTarget target,
        GpuRenderGraphTexture targetResource,
        bool markOutput = true)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        draw.Validate();
        target.Validate();
        RequireMatchingTarget(target, targetResource);

        GpuRenderGraphTexture[] sampledResources = ImportSampledTextures(
            draw.Material,
            (localName, texture) => context.ImportTexture(localName, texture.Texture, texture.Description),
            name);
        GpuRenderGraphBuffer[] bufferResources = ImportShaderBuffers(
            draw.Material,
            (localName, buffer) => context.ImportBuffer(localName, buffer.Buffer, buffer.Description),
            name);
        GpuRenderGraphShaderBindings? bindings = CreateShaderBindings(
            draw.Material, sampledResources, bufferResources);
        AddPass(context.AddPass, name, draw, target, targetResource, bindings);
        if (markOutput) { context.MarkOutput(targetResource); }
        return new(targetResource, sampledResources, bufferResources);
    }

    private static void AddPass(
        AddPassDelegate addPass,
        string name,
        DrawData draw,
        DrawRenderTarget target,
        GpuRenderGraphTexture targetResource,
        GpuRenderGraphShaderBindings? bindings)
    {
        var state = new PassState(draw, target, bindings);
        GpuRenderGraphPassBuilder builder = addPass(
            name,
            state,
            static (context, state) => Record(context, state),
            GpuRenderGraphPassFlags.None);
        if (bindings is not null) { builder.UseShaderBindings(bindings); }
        if (target.LoadOperation == GpuAttachmentLoadOperation.Load)
        {
            builder.ReadWrite(targetResource, GpuStage.ColorOutput);
        }
        else
        {
            builder.Write(targetResource, GpuStage.ColorOutput);
        }
    }

    private static void Record(GpuRenderGraphPassContextView context, PassState state)
    {
        GpuCommandBuffer commands = context.Commands
            .BeginRendering([
                new(
                    state.Target.View,
                    state.Target.LoadOperation,
                    state.Target.StoreOperation,
                    state.Target.ClearColor),
            ])
            .SetPipeline(state.Draw.Material.Pipeline);
        if (state.Bindings is { } bindings)
        {
            context.BindShaderResources(bindings);
        }

        Span<byte> transforms = stackalloc byte[128];
        Matrix4x4 world = state.Draw.Transforms.World;
        Matrix4x4 viewProjection = state.Draw.Transforms.ViewProjection;
        MemoryMarshal.Write(transforms, in world);
        MemoryMarshal.Write(transforms[64..], in viewProjection);

        commands
            .SetRootData(transforms)
            .SetViewportAndScissor(
                new(0, 0, state.Target.Description.Width, state.Target.Description.Height),
                new(0, 0, state.Target.Description.Width, state.Target.Description.Height))
            .Draw(state.Draw.Geometry.VertexCount, state.Draw.Geometry.InstanceCount)
            .EndRendering();
    }

    private static GpuRenderGraphTexture[] ImportSampledTextures(
        DrawMaterial material,
        Func<string, DrawSampledTexture, GpuRenderGraphTexture> import,
        string passName)
    {
        var result = new GpuRenderGraphTexture[material.SampledTextures.Count];
        for (int index = 0; index < result.Length; index++)
        {
            result[index] = import($"{passName}-material-texture-{index}", material.SampledTextures[index]);
        }
        return result;
    }

    private static GpuRenderGraphShaderBindings? CreateShaderBindings(
        DrawMaterial material,
        GpuRenderGraphTexture[] sampledResources,
        GpuRenderGraphBuffer[] bufferResources)
    {
        if (material.Resources is not { } resources) { return null; }
        var textures = new GpuRenderGraphShaderTextureBinding[sampledResources.Length];
        for (int index = 0; index < textures.Length; index++)
        {
            textures[index] = new(
                index,
                sampledResources[index],
                material.SampledTextures[index].Stages,
                resources.GetTexture(index));
        }
        var samplers = new List<GpuRenderGraphShaderSamplerBinding>();
        for (int index = 0; index < resources.SamplerSlotCount; index++)
        {
            SamplerId sampler = resources.GetSampler(index);
            if (!sampler.IsNull) { samplers.Add(new(index, sampler)); }
        }
        var buffers = new GpuRenderGraphShaderBufferBinding[bufferResources.Length];
        for (int index = 0; index < buffers.Length; index++)
        {
            DrawShaderBuffer buffer = material.ShaderBuffers[index];
            buffers[index] = new(
                buffer.Index,
                bufferResources[index],
                buffer.Stages,
                resources.GetBuffer(buffer.Index));
        }
        return textures.Length == 0 && samplers.Count == 0 && buffers.Length == 0
            ? null
            : new(textures, samplers, buffers);
    }

    private static GpuRenderGraphBuffer[] ImportShaderBuffers(
        DrawMaterial material,
        Func<string, DrawShaderBuffer, GpuRenderGraphBuffer> import,
        string passName)
    {
        var result = new GpuRenderGraphBuffer[material.ShaderBuffers.Count];
        for (int index = 0; index < result.Length; index++)
        {
            DrawShaderBuffer buffer = material.ShaderBuffers[index];
            result[index] = import($"{passName}-material-buffer-{buffer.Index}", buffer);
        }
        return result;
    }

    private static void RequireMatchingTarget(
        DrawRenderTarget target,
        GpuRenderGraphTexture targetResource)
    {
        if (target.Description != targetResource.Description)
        {
            throw new ArgumentException(
                "Target and render-graph texture descriptions do not match.",
                nameof(targetResource));
        }
    }

    private delegate GpuRenderGraphPassBuilder AddPassDelegate(
        string name,
        PassState state,
        GpuRenderGraphPassAction<PassState> record,
        GpuRenderGraphPassFlags flags);

    private readonly record struct PassState(
        DrawData Draw,
        DrawRenderTarget Target,
        GpuRenderGraphShaderBindings? Bindings);
}
