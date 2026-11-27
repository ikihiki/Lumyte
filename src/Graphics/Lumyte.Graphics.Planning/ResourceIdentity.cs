using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal sealed class ResourceIdentity(Func<IGraphicDevice, IDisposable> allocate, object? imported = null)
{
    internal Func<IGraphicDevice, IDisposable> Allocate { get; } = allocate;

    internal object? Imported { get; } = imported;

    internal List<PlanResource> Versions { get; } = [];

    internal int LatestVersion { get; set; }

    internal Func<IGraphicDevice, ulong> Measure { get; init; } = _ => 0;

    internal TextureFormat? TextureFormat { get; init; }

    internal uint MipLevels { get; init; } = 1;

    internal IGraphicsSurfaceFrame? SurfaceFrame { get; set; }

    internal bool Presentable { get; set; }

    internal TextureState InitialTextureState { get; init; } = TextureState.Undefined;
}
