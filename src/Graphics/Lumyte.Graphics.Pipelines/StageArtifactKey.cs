namespace Lumyte.Graphics.Pipelines;

/// <summary>Identifies an explicitly supplied implementation artifact.</summary>
public sealed record StageArtifactKey
{
    /// <summary>Initializes a new instance of the <see cref="StageArtifactKey"/> class.</summary>
    /// <param name="implementationType">The closed implementation type.</param>
    /// <param name="version">The positive contract version.</param>
    /// <param name="index">The nonnegative pass index.</param>
    public StageArtifactKey(Type implementationType, int version, int index)
    {
        ArgumentNullException.ThrowIfNull(implementationType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (implementationType.ContainsGenericParameters || implementationType.IsByRef || implementationType.IsPointer || implementationType == typeof(void))
        {
            throw new ArgumentException("An artifact requires a closed implementation type.", nameof(implementationType));
        }

        ImplementationType = implementationType;
        Version = version;
        Index = index;
    }

    /// <summary>Gets the implementation type.</summary>
    public Type ImplementationType { get; }

    /// <summary>Gets the version.</summary>
    public int Version { get; }

    /// <summary>Gets the pass index.</summary>
    public int Index { get; }
}
