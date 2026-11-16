using Lumyte.Graphics.Drawing2D;
using Lumyte.Maps.Mvt;
using Lumyte.Maps.UI;
using Lumyte.Resources;
using Lumyte.UI.Application;

namespace Lumyte.Maps.Sample;

/// <summary>Owns one CPU-only map session through the public application lifetime hooks.</summary>
public sealed class MapSampleContent : IUiApplicationContent, IUiApplicationGraphicsContent
{
    private readonly MapSession _map;

    /// <summary>Initializes a new instance of the <see cref="MapSampleContent"/> class.</summary>
    /// <param name="resources">Borrowed application manager.</param>
    /// <param name="registration">Explicit local fixture source registration.</param>
    public MapSampleContent(ResourceManager resources, LocalMvtRegistration registration)
    {
        var style = new MapStyle(MvtDecoder.SchemaId, "sample-v1", new(0.94f, 0.93f, 0.88f, 1), [
            new("water", new(0.2f, 0.5f, 0.8f, 0.6f)),
            new("buildings", new(0.7f, 0.3f, 0.2f, 0.7f)),
            new("roads", new(0.2f, 0.2f, 0.2f, 1), new StrokeStyle2D { Width = 12 }),
            new("roads", new(1, 0.9f, 0.6f, 1), new StrokeStyle2D { Width = 7 }),
        ]);
        _map = new(resources, new FixtureTileSet(registration), style, new MapSessionOptions(), [new(0, 0, 0)]);
        _map.SetCamera(new(0, 0, 0));
    }

    /// <summary>Gets a value indicating whether the local tile was adopted for the bounded smoke oracle.</summary>
    public bool HasLoadedTile => _map.PublishedTiles == 1;

    /// <inheritdoc/>
    public bool IsStopped => _map.IsStopped;

    /// <inheritdoc/>
    public ValueTask InitializeAsync(UiApplicationWindowContext context, CancellationToken cancellationToken) =>
        context.View.SetInitialRootAsync(new MapView { Session = _map, ClipToBounds = true, TestId = "map" }, cancellationToken);

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

    private sealed class FixtureTileSet(LocalMvtRegistration registration) : MapTileSet(
        "handmade", "v1", MvtDecoder.SchemaId, 0, 0, "Hand-authored Lumyte fixture; no OSM data", registration.Profile)
    {
        public override ResourceKey<MapTileContent> GetKey(MapTileId id) => registration.GetKey(id);
    }
}
