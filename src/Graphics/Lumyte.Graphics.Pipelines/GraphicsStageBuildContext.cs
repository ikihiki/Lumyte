namespace Lumyte.Graphics.Pipelines;

/// <summary>Provides only artifacts explicitly declared by this node.</summary>
public sealed class GraphicsStageBuildContext
{
    private readonly IReadOnlyDictionary<StageArtifactKey, StageArtifactLease> _artifacts;
    private readonly IReadOnlyList<StageArtifactKey> _declared;

    internal GraphicsStageBuildContext(IReadOnlyDictionary<StageArtifactKey, StageArtifactLease> artifacts, IReadOnlyList<StageArtifactKey> declared)
    {
        _artifacts = artifacts;
        _declared = declared;
    }

    /// <summary>Gets the declared artifact retained by the compiled build.</summary>
    /// <param name="key">The exact declared artifact identity.</param>
    /// <returns>The borrowed artifact lease; callers must not dispose it.</returns>
    public StageArtifactLease GetArtifact(StageArtifactKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!_declared.Contains(key) || !_artifacts.TryGetValue(key, out StageArtifactLease? artifact))
        {
            throw new ArgumentException("Artifact was not declared by this stage.", nameof(key));
        }

        artifact.Validate();
        return artifact;
    }
}
