namespace Lumyte.Graphics.Abstractions;

/// <summary>Owns a backend program and its native variants; the caller manages synchronization.</summary>
public interface IGraphicsComputePipeline : IDisposable
{
    /// <summary>Gets the immutable validated program description.</summary>
    ComputePipelineDesc Desc { get; }
}
