using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.ImageEffects;

/// <summary>Provides the borrowed device and ownership for fixed pass resources.</summary>
/// <param name="device">The borrowed device shared with the render graph.</param>
/// <param name="colorSemantic">The semantic preserved by appended effects.</param>
/// <param name="shaders">Optional borrowed shader artifacts or modules.</param>
public sealed class EffectPreparationContext(IGraphicDevice device, EffectColorSemantic colorSemantic, ImageEffectShaders? shaders = null) : IRenderGraphLifetime
{
    private readonly List<IDisposable> _resources = [];
    private bool _closing;
    private bool _disposed;

    /// <summary>Gets the caller-owned device.</summary>
    public IGraphicDevice Device { get; } = device;

    /// <summary>Gets the color semantic that all outputs of this preparation must preserve.</summary>
    public EffectColorSemantic ColorSemantic { get; } = colorSemantic;

    /// <summary>Gets the optional borrowed artifacts; null uses embedded defaults on demand.</summary>
    public ImageEffectShaders? Shaders { get; } = shaders;

    /// <summary>Transfers a fixed resource to this preparation context.</summary>
    /// <param name="resource">The resource disposed after GPU use and graph-plan disposal.</param>
    public void Own(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        EnsureAlive();
        _resources.Add(resource);
    }

    /// <summary>Releases fixed resources after graph temporary resources and GPU work have completed.</summary>
    /// <remarks>On failure, ownership is retained and Dispose can be retried.</remarks>
    public void Dispose() => Release();

    /// <inheritdoc/>
    void IRenderGraphLifetime.Validate() => EnsureAlive();

    internal T Add<T>(T resource)
        where T : IDisposable
    {
        Own(resource);
        return resource;
    }

    internal void Release()
    {
        if (_disposed)
        {
            return;
        }

        _closing = true;
        while (_resources.Count != 0)
        {
            try
            {
                _resources[^1].Dispose();
            }
            catch (Exception error)
            {
                throw new AggregateException("An effect resource failed to dispose.", error);
            }

            _resources.RemoveAt(_resources.Count - 1);
        }

        _disposed = true;
    }

    internal void EnsureAlive() => ObjectDisposedException.ThrowIf(_closing, this);

    internal void Close() => _closing = true;
}
