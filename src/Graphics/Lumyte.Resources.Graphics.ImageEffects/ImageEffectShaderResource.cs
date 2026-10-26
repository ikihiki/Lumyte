using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.ImageEffects;

namespace Lumyte.Resources.Graphics.ImageEffects;

/// <summary>A manager-owned image-effect shader pair tied to its original physical device epoch.</summary>
public sealed class ImageEffectShaderResource
{
    private readonly GraphicsResourceRuntime _runtime;
    private IGraphicsShader? _fullscreen;
    private IGraphicsShader? _effects;
    private ImageEffectShaders? _shaders;

    internal ImageEffectShaderResource(GraphicsResourceRuntime runtime, IGraphicDevice device, ulong epoch)
    {
        _runtime = runtime;
        Device = device;
        Epoch = epoch;
    }

    /// <summary>Gets the original device owning both shader modules.</summary>
    public IGraphicDevice Device { get; }

    /// <summary>Gets the original physical device epoch.</summary>
    public ulong Epoch { get; }

    /// <summary>Gets the borrowed modules, retained by the resource lease.</summary>
    public ImageEffectShaders Shaders => _shaders ?? throw new InvalidOperationException("The image-effect shaders are not available.");

    internal void Create(ImageEffectShaders artifacts, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _fullscreen = Device.CreateShader(artifacts.Fullscreen);
        cancellationToken.ThrowIfCancellationRequested();
        _effects = Device.CreateShader(artifacts.Effects);
        cancellationToken.ThrowIfCancellationRequested();
        _shaders = new(_fullscreen, _effects);
    }

    internal async ValueTask ReleaseAsync()
    {
        if (_fullscreen is null && _effects is null)
        {
            return;
        }

        if (Device.Status == GraphicsDeviceStatus.Lost)
        {
            await _runtime.WaitForRetirementAsync(Epoch);
        }

        _effects?.Dispose();
        _effects = null;
        _fullscreen?.Dispose();
        _fullscreen = null;
        _shaders = null;
    }
}
