using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing;

/// <summary>Loads portable offline drawing shaders without a runtime compiler or GPU.</summary>
public static class DrawingShaders
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<ShaderArtifact>> _artifacts = new(StringComparer.Ordinal);

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

    /// <summary>Loads the metallic-roughness model vertex shader.</summary>
    /// <returns>The WGSL and SPIR-V artifact.</returns>
    public static ShaderArtifact LoadModelVertex() => Load("model-vertex");

    /// <summary>Loads the metallic-roughness model fragment shader.</summary>
    /// <returns>The WGSL and SPIR-V artifact.</returns>
    public static ShaderArtifact LoadModelFragment() => Load("model-fragment");

    private static ShaderArtifact Load(string name) => _artifacts.GetOrAdd(name, static key => new(() => ShaderArtifact.LoadEmbedded(
        typeof(DrawingShaders).Assembly, $"Lumyte.Drawing.{key}.lshader"))).Value;
}
