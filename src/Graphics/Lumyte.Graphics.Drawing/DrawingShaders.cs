using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing;

/// <summary>Loads portable offline drawing shaders without a runtime compiler or GPU.</summary>
public static class DrawingShaders
{
    /// <summary>Loads the vertex-color vertex shader.</summary>
    /// <returns>The WGSL and SPIR-V artifact.</returns>
    public static ShaderArtifact LoadColorVertex() => Load("color-vertex");

    /// <summary>Loads the vertex-color fragment shader.</summary>
    /// <returns>The WGSL and SPIR-V artifact.</returns>
    public static ShaderArtifact LoadColorFragment() => Load("color-fragment");

    /// <summary>Loads the textured vertex shader.</summary>
    /// <returns>The WGSL and SPIR-V artifact.</returns>
    public static ShaderArtifact LoadTextureVertex() => Load("texture-vertex");

    /// <summary>Loads the textured fragment shader.</summary>
    /// <returns>The WGSL and SPIR-V artifact.</returns>
    public static ShaderArtifact LoadTextureFragment() => Load("texture-fragment");

    private static ShaderArtifact Load(string name) => ShaderArtifact.LoadEmbedded(
        typeof(DrawingShaders).Assembly, $"Lumyte.Drawing.{name}.lshader");
}
