using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Pipelines;

/// <summary>Exposes only the trusted originating graphics build identity.</summary>
internal interface IGraphicsPipelineInfo
{
    /// <summary>Gets the borrowed native device.</summary>
    IGraphicDevice Device { get; }

    /// <summary>Gets the fixed generation.</summary>
    GraphicsDeviceGeneration Generation { get; }

    /// <summary>Gets the immutable execution policy fixed by the build.</summary>
    GraphicsPipelinePolicy Policy { get; }

    /// <summary>Gets a value indicating whether explicit segmented execution is required.</summary>
    bool RequiresReadback { get; }

    /// <summary>Gets a value indicating whether the selected output consumes a canonical target write version.</summary>
    bool HasTargetWritesEndpoint { get; }

    /// <summary>Gets a value indicating whether results carry GPU record/execution readiness.</summary>
    bool HasGraphicsReadiness { get; }

    /// <summary>Gets the exact input value type.</summary>
    Type InputType { get; }

    /// <summary>Gets the exact output value type.</summary>
    Type OutputType { get; }

    /// <summary>Retains independent native-use ownership without transferring the caller's pipeline.</summary>
    /// <returns>The independent use lease.</returns>
    IDisposable AcquireUse();

    /// <summary>Rejects new preparations after closure or device loss.</summary>
    void ValidatePreparation();

    /// <summary>Gets an already acquired artifact without building or completing the pipeline.</summary>
    /// <param name="key">The exact declared build key.</param>
    /// <returns>The borrowed validated compiled artifact.</returns>
    StageArtifactLease GetCompiledArtifact(StageArtifactKey key);
}
