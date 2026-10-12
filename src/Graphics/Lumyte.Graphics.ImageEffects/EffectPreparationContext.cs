using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.ImageEffects;

/// <summary>Provides the borrowed device and ownership for fixed pass resources.</summary>
public sealed class EffectPreparationContext
{
    private readonly List<IDisposable> _resources = [];
    private bool _disposed;

    internal EffectPreparationContext(IGraphicDevice device, EffectColorSemantic colorSemantic)
    {
        Device = device;
        ColorSemantic = colorSemantic;
    }

    /// <summary>Gets the caller-owned device.</summary>
    public IGraphicDevice Device { get; }

    /// <summary>Gets the color semantic that all outputs of this preparation must preserve.</summary>
    public EffectColorSemantic ColorSemantic { get; }

    /// <summary>Transfers a fixed resource to the prepared effect owner.</summary>
    /// <param name="resource">The resource disposed after GPU use and graph-plan disposal.</param>
    public void Own(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _resources.Add(resource);
    }

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

        _disposed = true;
        List<Exception>? errors = null;
        for (int i = _resources.Count - 1; i >= 0; i--)
        {
            try
            {
                _resources[i].Dispose();
            }
            catch (Exception error)
            {
                (errors ??= []).Add(error);
            }
        }

        _resources.Clear();
        if (errors is not null)
        {
            throw new AggregateException("One or more effect resources failed to dispose.", errors);
        }
    }

    internal void EnsureAlive() => ObjectDisposedException.ThrowIf(_disposed, this);
}
