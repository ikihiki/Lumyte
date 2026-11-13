namespace Lumyte.Graphics.Abstractions;

/// <summary>Required host binding-group admission, distinct from optional device diagnostics.</summary>
internal interface IShaderReferenceAdmission : IShaderArgumentScope
{
    /// <inheritdoc/>
    void IShaderArgumentScope.Prepare(IShaderReferenceMetadata references)
    {
    }
}
