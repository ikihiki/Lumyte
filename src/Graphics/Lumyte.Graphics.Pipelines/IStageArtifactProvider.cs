using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Pipelines;

/// <summary>Supplies immutable completed programs without submitting work.</summary>
public interface IStageArtifactProvider
{
    /// <summary>Describes a program without allocating GPU resources.</summary>
    /// <param name="key">The declared artifact.</param>
    /// <param name="device">The borrowed target device.</param>
    /// <returns>The target, ABI and program identity.</returns>
    StageArtifactDescription Describe(StageArtifactKey key, IGraphicDevice device);

    /// <summary>Acquires a program after inventory preflight succeeds.</summary>
    /// <param name="key">The declared artifact.</param>
    /// <param name="context">The fixed device generation.</param>
    /// <returns>An independently owned lease.</returns>
    StageArtifactLease Acquire(StageArtifactKey key, StageArtifactBuildContext context);
}
