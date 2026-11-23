using Lumyte.Graphics.Drawing2D;
using Lumyte.Maps.Mvt;
using Lumyte.Maps.UI;
using Lumyte.Resources;
using Lumyte.UI.Application;

namespace Lumyte.Maps.Browser.Sample;

/// <summary>Owns one CPU-only map session through the public application lifetime hooks.</summary>
public sealed class BrowserMapContent : IUiApplicationContent, IUiApplicationGraphicsContent
{
    private readonly MapSession _map;

    /// <summary>Initializes a new instance of the <see cref="BrowserMapContent"/> class.</summary>
    /// <param name="resources">Borrowed application manager.</param>
    /// <param name="registration">Explicit immutable in-memory fixture source registration.</param>
    public BrowserMapContent(ResourceManager resources, InMemoryMvtRegistration registration)
    {
        var style = new MapStyle(MvtDecoder.SchemaId, "sample-v1", new(0.94f, 0.93f, 0.88f, 1), [
            new("water", new(0.2f, 0.5f, 0.8f, 0.6f)),
            new("buildings", new(0.7f, 0.3f, 0.2f, 0.7f)),
            new("roads", new(0.2f, 0.2f, 0.2f, 1), new StrokeStyle2D { Width = 12 }),
            new("roads", new(1, 0.9f, 0.6f, 1), new StrokeStyle2D { Width = 7 }),
        ]);
        _map = new(resources, new FixtureTileSet(registration), style, new MapSessionOptions { SelectVisibleTiles = true }, [new(0, 0, 0), new(1, 0, 0), new(1, 0, 1), new(1, 1, 0), new(1, 1, 1), new(2, 1, 1)]);
        _map.SetCamera(new(0, 0, 0));
    }

    /// <summary>Gets the adopted finite-universe tile count.</summary>
    public int PublishedTiles => _map.PublishedTiles;

    /// <summary>Gets the current interaction camera for the bounded smoke oracle.</summary>
    public MapCamera Camera => _map.Camera;

    /// <summary>Gets a value indicating whether the in-memory tile was adopted for the bounded smoke oracle.</summary>
    public bool HasLoadedTile => _map.PublishedTiles > 0;

    /// <inheritdoc/>
    public bool IsStopped => _map.IsStopped;

    /// <inheritdoc/>
    public ValueTask InitializeAsync(UiApplicationWindowContext context, CancellationToken cancellationToken)
    {
        BrowserProbe.Context = context;
        return context.View.SetInitialRootAsync(new MapView { Session = _map, ClipToBounds = true, TestId = "map" }, cancellationToken);
    }

    /// <inheritdoc/>
    public void Advance() => _map.Advance();

    /// <inheritdoc/>
    public void BeginStop() => _map.BeginStop();

    /// <inheritdoc/>
    public ValueTask RetireAsync() => _map.DisposeAsync();

    /// <inheritdoc/>
    public ValueTask ReleaseAsync() => ValueTask.CompletedTask;

    /// <inheritdoc/>
    public ValueTask RebuildGraphicsAsync(UiApplicationWindowContext context) => ValueTask.CompletedTask;

    private sealed class FixtureTileSet(InMemoryMvtRegistration registration) : MapTileSet(
        "handmade", "v1", MvtDecoder.SchemaId, 0, 2, "Hand-authored Lumyte fixture; no OSM data", registration.Profile)
    {
        public override ResourceKey<MapTileContent> GetKey(MapTileId id) => registration.GetKey(id);
    }
}
