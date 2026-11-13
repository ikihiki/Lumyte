using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

/// <summary>Retains backend-owned resource identities without shader bytes or wire fixups.</summary>
internal sealed class Direct3D12ReferenceMetadata : IShaderReferenceMetadata
{
    private Direct3D12ReferenceMetadata(IEnumerable<IShaderReference> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        References = Array.AsReadOnly(references.ToArray());
    }

    public IReadOnlyList<IShaderReference> References { get; }

    internal static IShaderReferenceMetadata Create(IEnumerable<IShaderReference> references) => new Direct3D12ReferenceMetadata(references);
}
