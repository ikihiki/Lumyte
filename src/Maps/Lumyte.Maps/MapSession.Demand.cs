using Lumyte.Resources;

namespace Lumyte.Maps;

/// <summary>Computes bounded spatial demand independently from resource dispatch.</summary>
public sealed partial class MapSession
{
    private readonly bool _spatial;
    private bool _demandDirty = true;
    private ulong _useSequence;

    /// <summary>Initializes a new instance of the <see cref="MapSession"/> class over the bounded normalized XYZ demand universe.</summary>
    /// <param name="resources">Borrowed shared resource manager.</param>
    /// <param name="tiles">Immutable content and profile registration.</param>
    /// <param name="style">Matching immutable style.</param>
    /// <param name="options">Spatial and resource admission budgets.</param>
    public MapSession(ResourceManager resources, MapTileSet tiles, MapStyle style, MapSessionOptions options)
        : this(resources, tiles, style, options, [])
    {
        if (options.MaxResidentTiles is <= 0 or > 65536 || options.MaxPendingTiles > 65536
            || options.PrefetchRadius is < 0 or > 2 || options.MaxPrefetchTiles < 0
            || options.MaxPrefetchTiles > options.MaxPendingTiles || options.MaxFallbackDepth is < 0 or > 22)
        {
            throw new ArgumentException("Spatial count, prefetch or fallback limits are invalid.", nameof(options));
        }

        _spatial = true;
    }

    /// <summary>Gets a value indicating whether visible demand exceeds MaxPendingTiles; overflow displays background without partial selection.</summary>
    public bool IsDemandOverflow { get; private set; }

    /// <summary>Gets a value indicating whether current demand is waiting for resident capacity or maintenance.</summary>
    public bool IsCapacityBlocked { get; private set; }

    /// <summary>Gets the current bounded undispatched demand count.</summary>
    public int PendingTiles => _stopping ? 0 : _entries.Count(entry => !entry.Dispatched && !entry.Retiring
        && (_spatial ? entry.Wanted : !_options.SelectVisibleTiles || IsVisible(entry.Id)));

    /// <summary>Switches immutable spatial content without mixing old and new display data.</summary>
    /// <param name="tiles">New schema-compatible content registration.</param>
    public void SetTileSet(MapTileSet tiles)
    {
        Enter(true);
        try
        {
            ArgumentNullException.ThrowIfNull(tiles);
            if (!_spatial || tiles.SchemaId != _style.SchemaId || tiles.LoadProfile.MaxReservationBytesPerTile > _options.MaxResidentCpuBytes)
            {
                throw new ArgumentException("Content switching requires a compatible spatial session.", nameof(tiles));
            }

            if (!ReferenceEquals(_tiles, tiles))
            {
                _tiles = tiles;
                Camera = Camera.Normalize(tiles.MinZoom, tiles.MaxZoom);
                _demandDirty = true;
                Notify();
            }
        }
        finally
        {
            _entered = false;
        }
    }

    private static int CompareIds(MapTileId left, MapTileId right) => left.Zoom != right.Zoom ? left.Zoom.CompareTo(right.Zoom)
        : left.X != right.X ? left.X.CompareTo(right.X) : left.Y.CompareTo(right.Y);

    private bool IsCurrent(Entry entry) => entry.Tiles is { } tiles && tiles.Id == _tiles.Id
        && tiles.ContentVersion == _tiles.ContentVersion && tiles.SchemaId == _tiles.SchemaId
        && tiles.LoadProfile.Fingerprint == _tiles.LoadProfile.Fingerprint
        && entry.Id.Zoom >= _tiles.MinZoom && entry.Id.Zoom <= _tiles.MaxZoom
        && tiles.GetKey(entry.Id).Equals(_tiles.GetKey(entry.Id))
        && tiles.GetExpectedSourceRevision(entry.Id) == _tiles.GetExpectedSourceRevision(entry.Id);

    private List<MapTileId> SelectVisible(out bool overflow)
    {
        overflow = false;
        var result = new List<MapTileId>();
        if (_viewport.X <= 0 || _viewport.Y <= 0)
        {
            return result;
        }

        int zoom = SourceZoom;
        int count = 1 << zoom;
        (double x, double y) = Camera.Project();
        double size = 512 * Math.Pow(2, Camera.Zoom - zoom);
        double left = Math.Floor((x * count) - (_viewport.X / (2 * size)));
        double right = Math.Ceiling((x * count) + (_viewport.X / (2 * size))) - 1;
        double top = Math.Max(0, Math.Floor((y * count) - (_viewport.Y / (2 * size))));
        double bottom = Math.Min(count - 1, Math.Ceiling((y * count) + (_viewport.Y / (2 * size))) - 1);
        double columns = Math.Min(count, right - left + 1);
        double rows = Math.Max(0, bottom - top + 1);
        if (!double.IsFinite(columns) || columns * rows > _options.MaxPendingTiles)
        {
            overflow = true;
            return result;
        }

        // Reduce before integer conversion; huge repeated worlds never produce a huge iteration range.
        int first = (int)(((left % count) + count) % count);
        for (int column = 0; column < (int)columns; column++)
        {
            for (int row = (int)top; row <= (int)bottom; row++)
            {
                result.Add(new(zoom, (first + column) % count, row));
            }
        }

        result.Sort(CompareIds);
        return result;
    }

    private void ReconcileDemand()
    {
        _demandDirty = false;
        List<MapTileId> visible = SelectVisible(out bool overflow);
        IsDemandOverflow = overflow;
        var candidates = new Dictionary<MapTileId, int>();
        foreach (MapTileId id in visible)
        {
            candidates.Add(id, 0);
        }

        int fallbackCapacity = _options.MaxPendingTiles - candidates.Count;
        var parents = new SortedSet<(double Distance, int Zoom, int X, int Y)>();
        if (fallbackCapacity > 0)
        {
            foreach (MapTileId id in visible)
            {
                if (_entries.Any(entry => entry.Id == id && entry.Lease is not null && !entry.Retiring && IsCurrent(entry)))
                {
                    continue;
                }

                MapTileId parent = id;
                for (int level = 0; level < _options.MaxFallbackDepth && parent.Zoom > _tiles.MinZoom; level++)
                {
                    parent = new(parent.Zoom - 1, parent.X / 2, parent.Y / 2);
                    parents.Add((DistanceToCamera(parent), parent.Zoom, parent.X, parent.Y));
                    if (parents.Count > fallbackCapacity)
                    {
                        parents.Remove(parents.Max);
                    }
                }
            }
        }

        foreach ((double _, int zoom, int x, int y) in parents)
        {
            candidates.Add(new(zoom, x, y), 1);
        }

        var prefetch = new SortedSet<(double Distance, int X, int Y)>();
        if (_options.MaxPrefetchTiles > 0)
        {
            foreach (MapTileId id in visible)
            {
                int count = 1 << id.Zoom;
                for (int dy = -_options.PrefetchRadius; dy <= _options.PrefetchRadius; dy++)
                {
                    for (int dx = -_options.PrefetchRadius; dx <= _options.PrefetchRadius; dx++)
                    {
                        int row = id.Y + dy;
                        if (row >= 0 && row < count)
                        {
                            MapTileId neighbor = new(id.Zoom, (id.X + dx + count) % count, row);
                            if (!candidates.ContainsKey(neighbor))
                            {
                                prefetch.Add((DistanceToCamera(neighbor), neighbor.X, neighbor.Y));
                                if (prefetch.Count > _options.MaxPrefetchTiles)
                                {
                                    prefetch.Remove(prefetch.Max);
                                }
                            }
                        }
                    }
                }
            }
        }

        foreach ((double _, int x, int y) in prefetch)
        {
            if (candidates.Count == _options.MaxPendingTiles)
            {
                break;
            }

            candidates.Add(new(SourceZoom, x, y), 2);
        }

        foreach (Entry entry in _entries)
        {
            entry.Wanted = false;
        }

        _entries.RemoveAll(entry => entry.Operation is null && entry.Lease is null && !entry.Retiring
            && (!IsCurrent(entry) || !candidates.ContainsKey(entry.Id)));
        foreach ((MapTileId id, int priority) in candidates)
        {
            Entry? entry = _entries.FirstOrDefault(value => value.Id == id && IsCurrent(value));
            if (entry is null)
            {
                // Retiring ledgers remain charged; wait for their maintenance before growing metadata.
                if (_entries.Count >= _options.MaxPendingTiles + _options.MaxResidentTiles + Math.Min(_options.MaxInFlightTiles, _options.MaxPendingTiles))
                {
                    IsCapacityBlocked = true;
                    _demandDirty = true;
                    continue;
                }

                entry = new(id) { Tiles = _tiles };
                _entries.Add(entry);
            }

            entry.Wanted = true;
            entry.Priority = priority;
            entry.Distance = DistanceToCamera(id);
            if (priority < 2 && entry.Lease is not null)
            {
                entry.LastUse = ++_useSequence;
            }
        }

        _entries.Sort((left, right) => CompareIds(left.Id, right.Id));
    }

    private double DistanceToCamera(MapTileId id)
    {
        (double x, double y) = Camera.Project();
        double count = 1 << id.Zoom;
        double dx = ((id.X + 0.5) / count) - x;
        dx -= Math.Round(dx);
        double dy = ((id.Y + 0.5) / count) - y;
        return (dx * dx) + (dy * dy);
    }
}
