namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes the compiled layout used by backend shader data storage.</summary>
public interface IShaderDataLayout
{
    /// <summary>Gets the logical shader type name.</summary>
    string Name { get; }

    /// <summary>Gets the element size in bytes.</summary>
    int Size { get; }

    /// <summary>Returns the compiled reference kind of a flattened member.</summary>
    /// <param name="path">The member path.</param>
    /// <returns>The reference kind.</returns>
    string ReferenceKind(string path);

    /// <summary>Checks whether another layout has the same compiled schema.</summary>
    /// <param name="layout">The layout to compare.</param>
    /// <returns>Whether the layouts match.</returns>
    bool Matches(IShaderDataLayout layout);

    /// <summary>Validates an application snapshot against the compiled schema.</summary>
    /// <param name="snapshot">The captured application values.</param>
    void Validate(ShaderValueSnapshot snapshot);

    /// <summary>Packs numeric values and backend-encoded resource references.</summary>
    /// <param name="snapshot">The captured application values.</param>
    /// <param name="reference">The backend reference encoder.</param>
    /// <returns>The packed bytes.</returns>
    byte[] Pack(ShaderValueSnapshot snapshot, Func<ShaderValue, string, byte[]> reference);
}
