namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes the compiled layout used by backend shader data storage.</summary>
public interface IShaderDataLayout
{
    /// <summary>Gets the logical shader type name.</summary>
    string Name { get; }

    /// <summary>Gets the element size in bytes.</summary>
    int Size { get; }

    /// <summary>Checks whether another layout has the same compiled schema.</summary>
    /// <param name="layout">The layout to compare.</param>
    /// <returns>Whether the layouts match.</returns>
    bool Matches(IShaderDataLayout layout);

    /// <summary>Copies C# wire bytes and fixes up backend-encoded resource reference slots.</summary>
    /// <param name="snapshot">The captured application values.</param>
    /// <param name="reference">The backend reference encoder.</param>
    /// <returns>The packed bytes.</returns>
    byte[] Pack(ShaderValueSnapshot snapshot, Func<ShaderValue, byte[]> reference);
}
