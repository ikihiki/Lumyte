using Lumyte.Pipelines;

namespace Lumyte.Graphics.Pipelines;

/// <summary>Provides explicitly bound graphics build and invocation capabilities.</summary>
public interface IGraphicsStageCapabilities : IStageCapabilities
{
    /// <summary>Gets this node's build capability.</summary>
    GraphicsStageBuildContext Build { get; }

    /// <summary>Gets this node's invocation capability.</summary>
    GraphicsStagePreparationContext Preparation { get; }
}
