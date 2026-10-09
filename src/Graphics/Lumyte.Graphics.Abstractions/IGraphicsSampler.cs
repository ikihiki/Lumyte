namespace Lumyte.Graphics.Abstractions;

/// <summary>Owns a backend sampler independently of texture storage and views.</summary>
/// <remarks>The caller manages resource lifetime and access synchronization; concurrent safety is not guaranteed.</remarks>
public interface IGraphicsSampler : IDisposable
{
    /// <summary>Gets the immutable sampling state used for creation, without implicit correction.</summary>
    SamplerDesc Desc { get; }
}
