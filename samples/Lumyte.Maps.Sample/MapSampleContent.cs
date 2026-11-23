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
    private int _smokeStep = -1;

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
        _map = new(resources, new FixtureTileSet(registration), style, new MapSessionOptions { MaxResidentTiles = 8, MaxPrefetchTiles = 2 });
        _map.SetCamera(new(0, 0, 0));
    }

    /// <summary>Gets the current interaction camera for the bounded smoke oracle.</summary>
    public MapCamera Camera => _map.Camera;

    /// <summary>Gets a value indicating whether the local tile was adopted for the bounded smoke oracle.</summary>
    public bool HasLoadedTile => _map.PublishedTiles > 0;

    /// <inheritdoc/>
    public bool IsStopped => _map.IsStopped;

    /// <summary>Advances a deterministic local path after demand settles; headless smoke verifies real manager reclamation.</summary>
    /// <returns>Whether all sixteen tile centers and a revisit were observed, with actual cleanup.</returns>
    public bool AdvanceSmokePath()
    {
        if (_smokeStep >= 0)
        {
            _map.Advance();
            if (_map.VisibleTiles.Count == 0)
            {
                return false;
            }
        }

        if (_map.Failures.Count != 0 || _map.MaintenanceFailure is not null)
        {
            throw new InvalidOperationException("Spatial sample resource processing failed.");
        }

        if (_smokeStep >= 0 && (_map.PendingTiles != 0 || _map.InFlightTiles != 0 || _map.IsMaintenanceInFlight
            || _map.ReadyVisibleTiles != _map.VisibleTiles.Count))
        {
            return false;
        }

        if (_smokeStep == 16)
        {
            if (_map.ReleasedGenerationCount == 0)
            {
                throw new InvalidOperationException("The bounded path must actually reclaim unused Manager generations.");
            }

            return true;
        }

        _smokeStep++;
        int index = _smokeStep % 16;
        double longitude = -135 + (90 * (index % 4));
        double y = ((index / 4) + 0.5) / 4;
        double latitude = Math.Atan(Math.Sinh(Math.PI * (1 - (2 * y)))) * 180 / Math.PI;
        _map.SetCamera(new(longitude, latitude, 2));
        return false;
    }

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
        "handmade", "v1", MvtDecoder.SchemaId, 0, 2, "Hand-authored Lumyte fixture; no OSM data", registration.Profile)
    {
        public override ResourceKey<MapTileContent> GetKey(MapTileId id) => registration.GetKey(id);
    }
}
