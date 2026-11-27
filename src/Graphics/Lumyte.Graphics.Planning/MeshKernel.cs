using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>A session-imported, independently leased mesh program.</summary>
/// <typeparam name="TArguments">The shader argument ABI.</typeparam>
public sealed class MeshKernel<TArguments>
    where TArguments : struct, IShaderArguments
{
    internal MeshKernel(GraphicsDeviceSession session, IMeshPipeline pipeline)
    {
        (Session, Pipeline) = (session, pipeline);
    }

    internal GraphicsDeviceSession Session { get; }

    internal IMeshPipeline Pipeline { get; }
}
