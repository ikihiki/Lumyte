using Lumyte.Graphics.Passes;

namespace Lumyte.Graphics.RenderGraph.Conformance;

public sealed record ImagePipelinePlan(
    GpuRenderGraphPlan Plan,
    GpuGraphInput<TextureClearValue> ColorInput,
    GpuRenderGraphTexture Output);

public sealed record ImagePresentationPlan(
    GpuRenderGraphPlan Plan,
    GpuGraphInput<TextureClearValue> ColorInput,
    GpuGraphTextureInput TargetInput);

/// <summary>Compiled once and used unchanged by Native and Portable conformance tests.</summary>
public static class ImagePipelineConsumer
{
    public static ImagePipelinePlan CreateExportPlan(
        GpuGraphTextureDescription description,
        TextureClearValue clearValue,
        OutputEncoding encoding = OutputEncoding.Linear,
        OutputAlphaMode alphaMode = OutputAlphaMode.Premultiplied,
        int copyCount = 1)
    {
        var graph = new GpuRenderGraph();
        GpuRenderGraphTexture output = graph.CreateTexture("output", description);
        GpuGraphInput<TextureClearValue> color = Populate(graph, output, clearValue, encoding, alphaMode, copyCount);
        graph.ExportTexture(output);
        return new(graph.Compile(), color, output);
    }

    public static ImagePresentationPlan CreatePresentationPlan(
        GpuGraphTextureDescription description,
        TextureClearValue clearValue,
        OutputEncoding encoding = OutputEncoding.Srgb,
        OutputAlphaMode alphaMode = OutputAlphaMode.Opaque)
    {
        var graph = new GpuRenderGraph();
        GpuGraphTextureInput output = graph.CreateTextureInput("presentation", description);
        GpuGraphInput<TextureClearValue> color = Populate(graph, output.Texture, clearValue, encoding, alphaMode);
        graph.MarkOutput(output.Texture);
        return new(graph.Compile(), color, output);
    }

    private static GpuGraphInput<TextureClearValue> Populate(
        GpuRenderGraph graph,
        GpuRenderGraphTexture output,
        TextureClearValue clearValue,
        OutputEncoding encoding,
        OutputAlphaMode alphaMode,
        int copyCount = 1)
    {
        GpuGraphTextureDescription working = output.Description with { Format = GpuFormat.Rgba8Unorm };
        GpuRenderGraphTexture source = graph.CreateTexture("source", working);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(copyCount);
        GpuGraphInput<TextureClearValue> color = graph.CreateInput("color", ClearValueInputContract.Instance, clearValue);
        graph.AddClearPass("clear", new(source, color));
        var copied = source;
        for (var index = 0; index < copyCount; index++)
        {
            var next = graph.CreateTexture($"copied{index}", working);
            graph.AddCopyPass($"copy{index}", new(copied, next));
            copied = next;
        }
        graph.AddOutputPass("output", new(copied, output, encoding, alphaMode));
        return color;
    }
}
