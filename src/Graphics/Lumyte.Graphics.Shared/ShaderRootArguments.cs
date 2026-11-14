using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

/// <summary>Retains immutable metadata for a selected root generation without owning its numeric payload.</summary>
public sealed class ShaderRootArguments
{
    private readonly Action<IReadOnlyCollection<IShaderReference>>? _admit;

    private ShaderRootArguments(string rootParameter, string shaderTypeName, int wireSize, IShaderReferenceMetadata references, Action<IReadOnlyCollection<IShaderReference>>? admit)
    {
        RootParameter = rootParameter;
        ShaderTypeName = shaderTypeName;
        WireSize = wireSize;
        References = references;
        _admit = admit;
    }

    /// <summary>Gets the logical root parameter name.</summary>
    public string RootParameter { get; }

    /// <summary>Gets the logical shader structure name.</summary>
    public string ShaderTypeName { get; }

    /// <summary>Gets the exact logical C# wire byte count.</summary>
    public int WireSize { get; }

    /// <summary>Gets immutable, non-owning root resource dependencies.</summary>
    public IShaderReferenceMetadata References { get; }

    /// <summary>Creates metadata for a completed backend-written root generation.</summary>
    /// <param name="rootParameter">The logical root parameter name.</param>
    /// <param name="shaderTypeName">The logical shader type name.</param>
    /// <param name="wireSize">The exact C# wire byte count.</param>
    /// <param name="references">The immutable backend-owned dependency metadata.</param>
    /// <param name="prepare">Optional reference-only preparation returning detached execution admission.</param>
    /// <returns>The immutable root generation metadata.</returns>
    public static ShaderRootArguments Create(string rootParameter, string shaderTypeName, int wireSize, IShaderReferenceMetadata references, Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootParameter);
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderTypeName);
        ArgumentOutOfRangeException.ThrowIfNegative(wireSize);
        ArgumentNullException.ThrowIfNull(references);
        Action<IReadOnlyCollection<IShaderReference>>? admit = prepare?.Invoke(references);
        return new(rootParameter, shaderTypeName, wireSize, references, admit);
    }

    /// <summary>Admits the refreshed complete transitive dependency set for each execution.</summary>
    /// <param name="references">All references reachable by this binding operation.</param>
    public void Admit(IReadOnlyCollection<IShaderReference> references) => _admit?.Invoke(references);
}
