using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Hosting;

/// <summary>Owns the actual backend instance; it does not forward device operations.</summary>
public interface IGraphicsDeviceOwner : IAsyncDisposable
{
    /// <summary>Gets the actual backend device.</summary>
    IGraphicDevice Device { get; }
}
