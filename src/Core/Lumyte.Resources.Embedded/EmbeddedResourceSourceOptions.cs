using System.Reflection;

namespace Lumyte.Resources.Embedded;

/// <summary>Configures immutable resources embedded in an already loaded assembly.</summary>
public sealed record EmbeddedResourceSourceOptions
{
    /// <summary>Gets the borrowed assembly containing the embedded data.</summary>
    public required Assembly Assembly { get; init; }

    /// <summary>Gets aliases from logical identifiers to exact manifest resource names. Unmapped identifiers use their value verbatim.</summary>
    /// <remarks>The source copies this mapping during construction. Names are ordinal and case-sensitive.</remarks>
    public IReadOnlyDictionary<ResourceId, string> ResourceNames { get; init; } = new Dictionary<ResourceId, string>();
}
