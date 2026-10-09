namespace Lumyte.Graphics.Abstractions;

/// <summary>Specifies a shader entry point stage.</summary>
public enum ShaderStage
{
    /// <summary>Uses the Vertex representation.</summary>
    Vertex,

    /// <summary>Uses the Fragment representation.</summary>
    Fragment,

    /// <summary>Uses the Compute representation.</summary>
    Compute,
}
