using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Library;

public static class ClearRenderGraphExtensions
{
    public static GpuRenderGraphTexture AddClear(
        this GpuRenderGraph graph,
        string name,
        DrawRenderTarget target,
        bool markOutput = true)
    {
        ArgumentNullException.ThrowIfNull(graph);
        Validate(name, target);
        GpuRenderGraphTexture texture = graph.ImportTexture(
            $"{name}-target", target.View.Texture, target.Description);
        return Add(graph.AddPass, graph.MarkOutput, name, target, texture, markOutput);
    }

    public static GpuRenderGraphTexture AddClear(
        this GpuRenderGraph graph,
        string name,
        DrawRenderTarget target,
        GpuRenderGraphTexture texture,
        bool markOutput = true)
    {
        ArgumentNullException.ThrowIfNull(graph);
        Validate(name, target, texture);
        return Add(graph.AddPass, graph.MarkOutput, name, target, texture, markOutput);
    }

    public static GpuRenderGraphTexture AddClear(
        this GpuRenderGraphContributionContext context,
        string name,
        DrawRenderTarget target,
        bool markOutput = true)
    {
        ArgumentNullException.ThrowIfNull(context);
        Validate(name, target);
        GpuRenderGraphTexture texture = context.ImportTexture(
            $"{name}-target", target.View.Texture, target.Description);
        return Add(context.AddPass, context.MarkOutput, name, target, texture, markOutput);
    }

    public static GpuRenderGraphTexture AddClear(
        this GpuRenderGraphContributionContext context,
        string name,
        DrawRenderTarget target,
        GpuRenderGraphTexture texture,
        bool markOutput = true)
    {
        ArgumentNullException.ThrowIfNull(context);
        Validate(name, target, texture);
        return Add(context.AddPass, context.MarkOutput, name, target, texture, markOutput);
    }

    private static GpuRenderGraphTexture Add(
        AddPassDelegate addPass,
        Func<GpuRenderGraphTexture, object> markOutput,
        string name,
        DrawRenderTarget target,
        GpuRenderGraphTexture texture,
        bool shouldMarkOutput)
    {
        addPass(name, target, static (context, value) => context.Commands
                .BeginRendering([new(
                    value.View,
                    GpuAttachmentLoadOperation.Clear,
                    value.StoreOperation,
                    value.ClearColor)])
                .EndRendering(),
                GpuRenderGraphPassFlags.None)
            .Write(texture, GpuStage.ColorOutput);
        if (shouldMarkOutput) { _ = markOutput(texture); }
        return texture;
    }

    private static void Validate(string name, DrawRenderTarget target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        target.Validate();
    }

    private static void Validate(
        string name,
        DrawRenderTarget target,
        GpuRenderGraphTexture texture)
    {
        Validate(name, target);
        if (target.Description != texture.Description)
        {
            throw new ArgumentException(
                "Target and render-graph texture descriptions do not match.",
                nameof(texture));
        }
    }

    private delegate GpuRenderGraphPassBuilder AddPassDelegate(
        string name,
        DrawRenderTarget state,
        GpuRenderGraphPassAction<DrawRenderTarget> record,
        GpuRenderGraphPassFlags flags);
}
