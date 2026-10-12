using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.ImageEffects;

/// <summary>Appends effect passes from parameters without exposing effect or DAG construction to callers.</summary>
public static class ImageEffectCompilerExtensions
{
    /// <summary>Appends ColorMatrix passes from parameters and returns the owned output.</summary>
    /// <param name="compiler">The borrowed compiler.</param>
    /// <param name="plan">The caller-owned render graph.</param>
    /// <param name="input">The graph-local input image.</param>
    /// <param name="matrix">The immutable color matrix.</param>
    /// <returns>The output and fixed-resource owner, disposed after GPU completion.</returns>
    public static PreparedImageEffect AppendColorMatrix(this ImageEffectCompiler compiler, RenderGraphBuilder plan, EffectImage input, ColorMatrix4x5 matrix)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        return compiler.AppendEffect(plan, new ColorMatrixEffect(matrix), input);
    }

    /// <summary>Appends GaussianBlur passes from parameters and returns the owned output.</summary>
    /// <param name="compiler">The borrowed compiler.</param>
    /// <param name="plan">The caller-owned render graph.</param>
    /// <param name="input">The graph-local input image.</param>
    /// <param name="sigmaX">The finite nonnegative horizontal device-pixel sigma.</param>
    /// <param name="sigmaY">The finite nonnegative vertical device-pixel sigma.</param>
    /// <returns>The output and fixed-resource owner, disposed after GPU completion.</returns>
    public static PreparedImageEffect AppendGaussianBlur(this ImageEffectCompiler compiler, RenderGraphBuilder plan, EffectImage input, float sigmaX, float sigmaY)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        return compiler.AppendEffect(plan, new GaussianBlurEffect(sigmaX, sigmaY), input);
    }

    /// <summary>Appends Opacity passes from parameters and returns the owned output.</summary>
    /// <param name="compiler">The borrowed compiler.</param>
    /// <param name="plan">The caller-owned render graph.</param>
    /// <param name="input">The graph-local input image.</param>
    /// <param name="opacity">The finite multiplier in [0, 1].</param>
    /// <returns>The output and fixed-resource owner, disposed after GPU completion.</returns>
    public static PreparedImageEffect AppendOpacity(this ImageEffectCompiler compiler, RenderGraphBuilder plan, EffectImage input, float opacity)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        return compiler.AppendEffect(plan, new OpacityEffect(opacity), input);
    }

    /// <summary>Appends Composite passes from parameters and returns the owned output.</summary>
    /// <param name="compiler">The borrowed compiler.</param>
    /// <param name="plan">The caller-owned render graph.</param>
    /// <param name="source">The graph-local source image.</param>
    /// <param name="backdrop">The graph-local backdrop image.</param>
    /// <param name="mode">The composition mode.</param>
    /// <param name="sourceOffsetX">The integer horizontal source translation.</param>
    /// <param name="sourceOffsetY">The integer vertical source translation.</param>
    /// <returns>The output and fixed-resource owner, disposed after GPU completion.</returns>
    public static PreparedImageEffect AppendComposite(this ImageEffectCompiler compiler, RenderGraphBuilder plan, EffectImage source, EffectImage backdrop, EffectCompositeMode mode, int sourceOffsetX = 0, int sourceOffsetY = 0)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        return compiler.AppendEffect(plan, new CompositeEffect(mode, sourceOffsetX, sourceOffsetY), source, backdrop);
    }
}
