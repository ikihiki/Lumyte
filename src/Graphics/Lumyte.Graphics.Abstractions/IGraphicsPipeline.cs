namespace Lumyte.Graphics.Abstractions;

/// <summary>Owns a backend program and its native variants; the caller manages synchronization.</summary>
public interface IGraphicsPipeline : IDisposable
{
    /// <summary>Gets the immutable validated program description.</summary>
    GraphicsPipelineDesc Desc { get; }
}
