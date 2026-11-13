namespace Lumyte.Graphics.Abstractions;

/// <summary>Stores immutable, non-owning resource dependencies without wire locations.</summary>
public sealed class ShaderDependencyMetadata : IShaderReferenceMetadata
{
    /// <summary>Initializes a new instance of the <see cref="ShaderDependencyMetadata"/> class with owned resource identities.</summary>
    /// <param name="references">The resource reference identities.</param>
    public ShaderDependencyMetadata(IEnumerable<IShaderReference> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        IShaderReference[] copy = references.ToArray();
        if (copy.Any(reference => reference is null))
        {
            throw new ArgumentException("A shader reference must originate from an argument table.", nameof(references));
        }

        References = Array.AsReadOnly(copy);
    }

    /// <inheritdoc/>
    public IReadOnlyList<IShaderReference> References { get; }
}
