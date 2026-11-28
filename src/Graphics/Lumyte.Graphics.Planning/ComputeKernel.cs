using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>Declares compute kernel.</summary>
/// <typeparam name = "TArguments">The typed shader or resource contract.</typeparam>
public sealed class ComputeKernel<TArguments>
    where TArguments : struct, IShaderArguments
{
    internal ComputeKernel(GraphicsDeviceSession session, IGraphicsComputePipeline pipeline)
    {
        (Session, Pipeline) = (session, pipeline);
    }

    internal GraphicsDeviceSession Session { get; }

    internal IGraphicsComputePipeline Pipeline { get; }
}
