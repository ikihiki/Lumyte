using System.Collections;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

/// <summary>Retains backend-owned resource identities without shader bytes or wire fixups.</summary>
internal sealed class Direct3D12ReferenceMetadata : IShaderReferenceMetadata, IReadOnlyList<IShaderReference>
{
    private static readonly Direct3D12ReferenceMetadata _empty = new(Array.Empty<IShaderReference>());
    private readonly IShaderReference[] _references;

    private Direct3D12ReferenceMetadata(IShaderReference[] references)
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

    internal static IShaderReferenceMetadata TakeOwnership(IShaderReference[] references) => references.Length == 0 ? _empty : new Direct3D12ReferenceMetadata(references);
}
