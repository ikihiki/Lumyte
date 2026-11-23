using Lumyte.Graphics.Abstractions;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Pipelines;

/// <summary>Captures finite device requirements without providing execution authority.</summary>
public sealed class GraphicsStageRequirements : IStageExtensionRequirement
{
    /// <summary>Initializes a new instance of the <see cref="GraphicsStageRequirements"/> class.</summary>
    /// <param name="domain">The explicit operation domain.</param>
    /// <param name="maxDeviceBytes">The nonnegative device ceiling.</param>
    /// <param name="artifacts">The unique artifact keys.</param>
    /// <param name="inputAccesses">One complete access declaration per used input.</param>
    public GraphicsStageRequirements(GraphicsExecutionDomain domain, long maxDeviceBytes, IReadOnlyList<StageArtifactKey> artifacts, IReadOnlyList<StageInputAccess> inputAccesses)
    {
        if (!Enum.IsDefined(domain))
        {
            throw new ArgumentException("Unknown graphics domain.", nameof(domain));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(maxDeviceBytes);
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(inputAccesses);
        StageArtifactKey[] keys = artifacts.ToArray();
        StageInputAccess[] accesses = inputAccesses.ToArray();
        if (keys.Any(key => key is null) || keys.Distinct().Count() != keys.Length)
        {
            throw new ArgumentException("Artifact keys must be nonnull and unique.", nameof(artifacts));
        }

        if (accesses.Any(access => access is null) || accesses.Select(access => access.InputIndex).Distinct().Count() != accesses.Length)
        {
            throw new ArgumentException("Inputs require unique nonnull access declarations.", nameof(inputAccesses));
        }

        ResourceAccess allowed = domain switch
        {
            GraphicsExecutionDomain.Compute => ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite | ResourceAccess.IndirectRead | ResourceAccess.CopyRead | ResourceAccess.CopyWrite,
            GraphicsExecutionDomain.Raster or GraphicsExecutionDomain.Mesh => ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite | ResourceAccess.ColorRead | ResourceAccess.ColorWrite | ResourceAccess.DepthStencilRead | ResourceAccess.DepthStencilWrite | ResourceAccess.IndexRead | ResourceAccess.IndirectRead | ResourceAccess.CopyRead | ResourceAccess.CopyWrite,
            GraphicsExecutionDomain.Upload or GraphicsExecutionDomain.Download => ResourceAccess.CopyRead | ResourceAccess.CopyWrite,
            _ => ResourceAccess.None,
        };
        if (accesses.Any(access => (access.Access & ~allowed) != 0) ||
            ((domain is GraphicsExecutionDomain.Upload or GraphicsExecutionDomain.Download) && keys.Length != 0))
        {
            throw new ArgumentException("Access or artifacts conflict with the execution domain.");
        }

        Domain = domain;
        MaxDeviceBytes = maxDeviceBytes;
        Artifacts = Array.AsReadOnly(keys);
        InputAccesses = Array.AsReadOnly(accesses);
    }

    /// <summary>Gets the operation domain.</summary>
    public GraphicsExecutionDomain Domain { get; }

    /// <summary>Gets the device ceiling.</summary>
    public long MaxDeviceBytes { get; }

    /// <summary>Gets the immutable artifact keys.</summary>
    public IReadOnlyList<StageArtifactKey> Artifacts { get; }

    /// <summary>Gets the immutable input accesses.</summary>
    public IReadOnlyList<StageInputAccess> InputAccesses { get; }
}
