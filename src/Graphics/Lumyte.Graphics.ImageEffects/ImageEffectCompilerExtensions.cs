using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.ImageEffects;

/// <summary>Appends existing effects directly to a render graph; never constructs effects or effect graphs.</summary>
public static class ImageEffectCompilerExtensions
{
    /// <summary>Appends the existing ColorMatrix effect's passes and returns its owned output.</summary>
    /// <param name="compiler">The borrowed compiler.</param>
    /// <param name="plan">The caller-owned render graph.</param>
    /// <param name="effect">The existing effect; its implementation cannot be replaced.</param>
    /// <param name="input">The graph-local input image.</param>
    /// <returns>The output and fixed-resource owner, disposed after GPU completion.</returns>
    public static PreparedImageEffect AppendColorMatrix(this ImageEffectCompiler compiler, RenderGraphBuilder plan, ColorMatrixEffect effect, EffectImage input)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        return compiler.AppendEffect(plan, effect, input);
    }

    /// <summary>Appends the existing GaussianBlur effect's passes and returns its owned output.</summary>
    /// <param name="compiler">The borrowed compiler.</param>
    /// <param name="plan">The caller-owned render graph.</param>
    /// <param name="effect">The existing effect; its implementation cannot be replaced.</param>
    /// <param name="input">The graph-local input image.</param>
    /// <returns>The output and fixed-resource owner, disposed after GPU completion.</returns>
    public static PreparedImageEffect AppendGaussianBlur(this ImageEffectCompiler compiler, RenderGraphBuilder plan, GaussianBlurEffect effect, EffectImage input)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        return compiler.AppendEffect(plan, effect, input);
    }

    /// <summary>Appends the existing Opacity effect's passes and returns its owned output.</summary>
    /// <param name="compiler">The borrowed compiler.</param>
    /// <param name="plan">The caller-owned render graph.</param>
    /// <param name="effect">The existing effect; its implementation cannot be replaced.</param>
    /// <param name="input">The graph-local input image.</param>
    /// <returns>The output and fixed-resource owner, disposed after GPU completion.</returns>
    public static PreparedImageEffect AppendOpacity(this ImageEffectCompiler compiler, RenderGraphBuilder plan, OpacityEffect effect, EffectImage input)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        return compiler.AppendEffect(plan, effect, input);
    }

    /// <summary>Appends the existing Composite effect's passes and returns its owned output.</summary>
    /// <param name="compiler">The borrowed compiler.</param>
    /// <param name="plan">The caller-owned render graph.</param>
    /// <param name="effect">The existing effect; its implementation cannot be replaced.</param>
    /// <param name="source">The graph-local source image.</param>
    /// <param name="backdrop">The graph-local backdrop image.</param>
    /// <returns>The output and fixed-resource owner, disposed after GPU completion.</returns>
    public static PreparedImageEffect AppendComposite(this ImageEffectCompiler compiler, RenderGraphBuilder plan, CompositeEffect effect, EffectImage source, EffectImage backdrop)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        return compiler.AppendEffect(plan, effect, source, backdrop);
    }
}
