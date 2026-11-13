namespace Lumyte.Graphics.Abstractions;

/// <summary>Prepares root references at selection and admits transitive references at execution.</summary>
public interface IShaderArgumentScope
{
    /// <summary>Prepares the immutable root reference identities during argument selection.</summary>
    /// <param name="references">The selected root reference metadata.</param>
    void Prepare(ShaderReferenceMetadata references);

    /// <summary>Admits all references reachable by one indivisible native binding operation.</summary>
    /// <param name="references">The complete root and transitive reference set.</param>
    void Admit(IReadOnlyCollection<IShaderReference> references);
}
