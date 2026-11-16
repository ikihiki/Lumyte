using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Benchmarks.Testing;
using Lumyte.Graphics.RenderGraph;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.ImageEffects.Benchmark.Preparation;

/// <summary>Actual public built-in effect fixtures shared with CPU-only tests.</summary>
public static class EffectWorkloads
{
    /// <summary>Returns the bounded HDR source descriptor; dimensions are not a fill-rate measurement.</summary>
    /// <returns>The source requirements.</returns>
    public static TextureDesc SourceDescription() => new() { Width = 64, Height = 64, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.Sampled | TextureUsage.RenderAttachment };

    /// <summary>Creates an immutable DAG with nonzero blur and nonidentity opacity.</summary>
    /// <param name="scenario">The bounded scenario.</param>
    /// <returns>The reusable definition.</returns>
    public static ImageEffectGraph Definition(EffectScenario scenario)
    {
        var source = ImageEffectGraph.Source();
        var blur = ImageEffectGraph.Create(new GaussianBlurEffect(2, 3), source);
        return scenario switch
        {
            EffectScenario.Blur => blur,
            EffectScenario.BlurComposite => ImageEffectGraph.Create(new CompositeEffect(EffectCompositeMode.SrcOver), ImageEffectGraph.Create(new OpacityEffect(0.6f), blur), source),
            EffectScenario.SharedBranch => ImageEffectGraph.Create(new CompositeEffect(EffectCompositeMode.Plus), ImageEffectGraph.Create(new OpacityEffect(0.4f), blur), ImageEffectGraph.Create(new PostProcessEffect(1, 0.8f, 0.2f), blur)),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
    }

    /// <summary>Expands built-in preparation and exports its meaningful sampled output.</summary>
    /// <param name="device">The borrowed recording device.</param>
    /// <param name="source">The borrowed initialized input.</param>
    /// <param name="effects">The immutable effect DAG.</param>
    /// <returns>The caller-owned graph and all its fixed preparation.</returns>
    public static Graph Build(CpuGraphicsDevice device, IGraphicsTexture source, ImageEffectGraph effects)
    {
        var graph = new Graph(device);
        try
        {
            RenderGraphTexture input = graph.ImportTexture(source, TextureState.Sampled, new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead));
            EffectImage output = graph.AppendEffects(effects, new(input, new(0, 0, 64, 64), EffectColorSemantic.DisplayLinear));
            graph.ExportTexture(output.Texture, TextureState.Sampled, new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead));
            return graph;
        }
        catch
        {
            graph.Dispose();
            throw;
        }
    }
}
