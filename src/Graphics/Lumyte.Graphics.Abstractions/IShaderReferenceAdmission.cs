namespace Lumyte.Graphics.Abstractions;

/// <summary>Required host binding-group admission, distinct from optional device diagnostics.</summary>
internal interface IShaderReferenceAdmission
{
    /// <summary>Admits the resources needed by one indivisible native binding operation.</summary>
    /// <param name="references">The complete reachable resource set.</param>
    void Admit(IReadOnlyCollection<IShaderReference> references);
}
