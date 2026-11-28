using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>Declares raster kernel.</summary>
/// <typeparam name = "TArguments">The typed shader or resource contract.</typeparam>
public sealed class RasterKernel<TArguments>
    where TArguments : struct, IShaderArguments
{
    internal RasterKernel(GraphicsDeviceSession session, IGraphicsPipeline pipeline)
    {
        (Session, Pipeline) = (session, pipeline);
    }

    internal GraphicsDeviceSession Session { get; }

    internal IGraphicsPipeline Pipeline { get; }
}
