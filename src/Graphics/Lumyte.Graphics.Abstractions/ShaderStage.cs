namespace Lumyte.Graphics.Abstractions;

/// <summary>Specifies a shader entry point stage.</summary>
public enum ShaderStage
{
    /// <summary>Uses the Vertex representation.</summary>
    Vertex = 0,

    /// <summary>Uses the Fragment representation.</summary>
    Fragment = 1,

    /// <summary>Uses the Compute representation.</summary>
    Compute = 2,
}
