using System.Collections;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Vulkan;

/// <summary>Retains backend-owned resource identities without shader bytes or wire fixups.</summary>
internal sealed class VulkanReferenceMetadata : IShaderReferenceMetadata, IReadOnlyList<IShaderReference>
{
    private static readonly VulkanReferenceMetadata _empty = new(Array.Empty<IShaderReference>());
    private readonly IShaderReference[] _references;

    private VulkanReferenceMetadata(IShaderReference[] references)
    {
        _references = references;
    }

    /// <summary>Gets the immutable ordered references.</summary>
    public IReadOnlyList<IShaderReference> References => this;

    /// <summary>Gets the number of physical reference slots.</summary>
    public int Count => _references.Length;

    /// <summary>Gets the reference in the specified slot.</summary>
    /// <param name="index">The physical slot index.</param>
    /// <returns>The retained reference identity.</returns>
    public IShaderReference this[int index] => _references[index];

    /// <summary>Enumerates the ordered reference identities.</summary>
    /// <returns>The reference enumerator.</returns>
    public IEnumerator<IShaderReference> GetEnumerator() => ((IEnumerable<IShaderReference>)_references).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal static IShaderReferenceMetadata Merge(IShaderReferenceMetadata first, IShaderReferenceMetadata second)
    {
        if (ReferenceEquals(first, second))
        {
            return first;
        }

        var references = new HashSet<IShaderReference>(first.References);
        references.UnionWith(second.References);
        return TakeOwnership(references.ToArray());
    }

    internal static IShaderReferenceMetadata TakeOwnership(IShaderReference[] references) => references.Length == 0 ? _empty : new VulkanReferenceMetadata(references);
}
