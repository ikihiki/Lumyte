namespace Lumyte.Pipelines;

/// <summary>Captures immutable input extents for requirement validation.</summary>
public sealed class StageInputDescriptions
{
    internal StageInputDescriptions(IEnumerable<SchemaExtent> extents, long maxHostBytes = long.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxHostBytes);
        Extents = Array.AsReadOnly(extents.ToArray());
        MaxHostBytes = maxHostBytes;
    }

    /// <summary>Gets captured input extents.</summary>
    public IReadOnlyList<SchemaExtent> Extents { get; }

    /// <summary>Gets the explicit receiving host ceiling available to bounded metadata analysis.</summary>
    public long MaxHostBytes { get; }
}
