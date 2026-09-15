using Lumyte.Graphics.Passes;
using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.RenderGraph.Conformance;

/// <summary>The exact same consumer assembly is executed by Native and Portable conformance tests.</summary>
public static class TwoDRenderConsumer
{
    public const int Size = 64;

    public static TwoDRenderPlan CreatePlan(Draw2DScene scene, GpuFormat format = GpuFormat.Rgba8Unorm)
    {
        var graph = new GpuRenderGraph();
        var target = graph.CreateTexture("2d.color", new(Size, Size, format));
        graph.AddClearPass("initialize", new(target, TextureClearValue.Color(default)));
        var input = graph.CreateInput("2d.scene", Draw2DSceneInputContract.Instance, scene);
        graph.Add2DPass("2d", new(input, target));
        graph.ExportTexture(target);
        return new(graph.Compile(), input, target);
    }

    public static (TwoDRenderPlan Plan, Draw2DScene Reference) CreateTexturePlan()
    {
        var graph = new GpuRenderGraph();
        var source = graph.CreateTexture("2d.source", new(Size, Size, GpuFormat.Rgba8Unorm));
        var target = graph.CreateTexture("2d.color", new(Size, Size, GpuFormat.Rgba8Unorm));
        Draw2DScene sourceScene = TwoDScenarios.Create("shapes");
        graph.AddClearPass("initialize source", new(source, TextureClearValue.Color(default)));
        graph.Add2DPass("draw source", new(sourceScene, source));
        graph.AddClearPass("initialize target", new(target, TextureClearValue.Color(default)));
        using var drawing = new Draw2DSceneBuilder();
        drawing.DrawImage(source, new(8, 8, 48, 48));
        var input = graph.CreateInput("2d.scene", Draw2DSceneInputContract.Instance, drawing.Finish());
        graph.Add2DPass("sample source", new(input, target, [source]));
        graph.ExportTexture(target);

        var referencePixels = SkiaTwoDReference.Render(sourceScene);
        var referenceImage = new GpuImageUploadData(new("2d.reference-source", 1), new(Size, Size, GpuFormat.Rgba8Unorm),
            GpuImageColorEncoding.Linear, GpuImageAlphaMode.Premultiplied, [new(0, 0, Size * 4, Size * Size * 4, referencePixels)]);
        using var reference = new Draw2DSceneBuilder();
        reference.DrawImage(referenceImage, new(8, 8, 48, 48));
        return (new(graph.Compile(), input, target), reference.Finish());
    }
}

public sealed record TwoDRenderPlan(GpuRenderGraphPlan Plan, GpuGraphInput<Draw2DScene> Scene, GpuRenderGraphTexture Output);
