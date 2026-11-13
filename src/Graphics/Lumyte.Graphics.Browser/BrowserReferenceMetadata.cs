using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

/// <summary>Retains backend-owned resource identities without shader bytes or wire fixups.</summary>
internal sealed class BrowserReferenceMetadata : IShaderReferenceMetadata
{
    private BrowserReferenceMetadata(IEnumerable<IShaderReference> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        References = Array.AsReadOnly(references.ToArray());
    }

    public IReadOnlyList<IShaderReference> References { get; }

    internal static IShaderReferenceMetadata Create(IEnumerable<IShaderReference> references) => new BrowserReferenceMetadata(references);
}
