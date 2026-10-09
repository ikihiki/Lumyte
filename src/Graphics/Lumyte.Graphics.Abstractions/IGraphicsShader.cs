namespace Lumyte.Graphics.Abstractions;

/// <summary>Owns one backend shader module created from a compiled artifact.</summary>
/// <remarks>The caller manages CPU and GPU access and lifetime synchronization.</remarks>
public interface IGraphicsShader : IDisposable
{
    /// <summary>Gets the artifact used for module creation.</summary>
    ShaderArtifact Artifact { get; }
}
