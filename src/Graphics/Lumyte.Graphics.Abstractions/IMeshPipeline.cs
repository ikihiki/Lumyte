namespace Lumyte.Graphics.Abstractions;

/// <summary>Owns a mesh program and its native variants; callers retain borrowed shaders until GPU completion.</summary>
public interface IMeshPipeline : IDisposable
{
    /// <summary>Gets the immutable program description.</summary>
    MeshPipelineDesc Desc { get; }
}
