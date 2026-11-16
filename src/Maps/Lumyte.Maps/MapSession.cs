using System.Numerics;
using Lumyte.Execution;
using Lumyte.Resources;

namespace Lumyte.Maps;

/// <summary>Owns bounded explicit local tiles with fixed or viewport-selected admission; no HTTP.</summary>
public sealed partial class MapSession : IAsyncDisposable
{
    private readonly ResourceManager _resources;
    private readonly MapMaintenanceCoordinator _coordinator;
    private readonly MapStyle _style;
    private readonly MapSessionOptions _options;
    private readonly List<Entry> _entries;
    private MapTileSet _tiles;
    private Vector2 _viewport;
    private object? _attachment;
    private Task? _disposeTask;
    private bool _stopping;
    private bool _disposed;
    private bool _entered;

    /// <summary>Initializes a new instance of the <see cref="MapSession"/> class.</summary>
    /// <param name="resources">Borrowed manager, outliving this session.</param>
    /// <param name="tiles">Immutable tile set registration.</param>
    /// <param name="style">Schema-bound paint rules.</param>
    /// <param name="options">Admission budgets.</param>
    /// <param name="fixedTiles">Explicit normalized fixed demand, bounded before copying.</param>
    public MapSession(ResourceManager resources, MapTileSet tiles, MapStyle style, MapSessionOptions options, IReadOnlyList<MapTileId> fixedTiles)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fixedTiles);
        if (tiles.SchemaId != style.SchemaId || options.MaxPendingTiles <= 0 || options.MaxInFlightTiles <= 0
            || options.MaxCompletedTiles <= 0 || options.MaxPublicationsPerAdvance <= 0 || options.MaxRecordingCpuBytes <= 0
            || options.MaxResidentCpuBytes < tiles.LoadProfile.MaxReservationBytesPerTile
            || fixedTiles.Count > options.MaxPendingTiles)
        {
            throw new ArgumentException("Schema or fixed-session budgets are invalid.", nameof(options));
        }

        if (style.Layers.Any(layer => layer.SourceLayer is not "roads" and not "water" and not "buildings"))
        {
            throw new ArgumentException("The initial map schema supports roads, water and buildings only.", nameof(style));
        }

        _resources = resources;
        _coordinator = MapMaintenanceCoordinator.For(resources);
        _tiles = tiles;
        _style = style;
        _options = options;
        _entries = new(fixedTiles.Count);
        foreach (MapTileId id in fixedTiles)
        {
            MapTileId normalized = id.Normalize();
            if (normalized.Zoom < tiles.MinZoom || normalized.Zoom > tiles.MaxZoom)
            {
                throw new ArgumentOutOfRangeException(nameof(fixedTiles), "Fixed tile zoom is outside the manifest range.");
            }

            _ = tiles.GetKey(normalized);
            if (_entries.Any(entry => entry.Id == normalized))
            {
                throw new ArgumentException("Fixed demand contains a duplicate normalized tile.", nameof(fixedTiles));
            }

            if (!options.SelectVisibleTiles && _entries.Count > 0 && _entries[0].Id.Zoom != normalized.Zoom)
            {
                throw new ArgumentException("M2 fixed sessions require a single zoom; overlapping parent/child fallback is not supported.", nameof(fixedTiles));
            }

            _entries.Add(new(normalized));
        }

        _entries.Sort(static (left, right) => left.Id.Zoom != right.Id.Zoom ? left.Id.Zoom.CompareTo(right.Id.Zoom)
            : left.Id.X != right.Id.X ? left.Id.X.CompareTo(right.Id.X) : left.Id.Y.CompareTo(right.Id.Y));
        Camera = new MapCamera(0, 0, tiles.MinZoom);
    }

    /// <summary>Notifies the serialized UI owner of an adopted display change.</summary>
    public event Action? Changed;

    /// <summary>Gets the normalized camera.</summary>
    public MapCamera Camera { get; private set; }

    /// <summary>Gets the display dependency revision.</summary>
    public ulong DisplayRevision { get; private set; }

    /// <summary>Gets a value indicating whether stop was requested and all retains reached terminal collection.</summary>
    public bool IsStopped => _spatial ? SpatialStopped : _stopping && _entries.All(entry => entry.Operation is null);

    /// <summary>Gets storage charged in full even for shared manager cache hits.</summary>
    public long ResidentCpuBytes => _entries.Sum(entry => entry.Charge);

    /// <summary>Gets the number of terminally-owned operations, including uncollected completions.</summary>
    public int InFlightTiles => _entries.Count(entry => entry.Operation is not null);

    /// <summary>Gets the adopted tile count.</summary>
    public int PublishedTiles => _entries.Count(entry => entry.Lease is not null);

    /// <summary>Gets integer acquisition zoom; continuous camera scale is applied only when recording.</summary>
    public int SourceZoom => Math.Clamp((int)Math.Floor(Camera.Zoom), _tiles.MinZoom, _tiles.MaxZoom);

    /// <summary>Gets normalized visible known IDs in deterministic tile order; repeated worlds share an ID.</summary>
    public IReadOnlyList<MapTileId> VisibleTiles => _spatial ? SelectVisible(out _) : _entries.Where(entry => IsVisible(entry.Id)).Select(entry => entry.Id).ToArray();

    /// <summary>Gets failed tile diagnostics in stable fixed-set order.</summary>
    public IReadOnlyList<Exception> Failures => _entries.Where(entry => entry.Error is not null).Select(entry => entry.Error!).ToArray();

    /// <summary>Claims the sole UI attachment without taking session ownership.</summary>
    /// <param name="owner">Stable element identity.</param>
    public void Attach(object owner)
    {
        EnsureAvailable();
        if (_entered || _stopping)
        {
            throw new InvalidOperationException("Cannot attach during a session operation or after stop.");
        }

        ArgumentNullException.ThrowIfNull(owner);
        if (_attachment is not null)
        {
            throw new InvalidOperationException("A map session supports only one mounted view.");
        }

        _attachment = owner;
    }

    /// <summary>Returns the UI attachment without stopping the session.</summary>
    /// <param name="owner">The attached element.</param>
    public void Detach(object owner)
    {
        if (!ReferenceEquals(_attachment, owner))
        {
            throw new InvalidOperationException("Only the attached owner may detach.");
        }

        _attachment = null;
    }

    /// <summary>Changes projection and finite-set demand without starting I/O.</summary>
    /// <param name="camera">Finite camera coordinates.</param>
    public void SetCamera(MapCamera camera)
    {
        Enter(true);
        try
        {
            MapCamera normalized = camera.Normalize(_tiles.MinZoom, _tiles.MaxZoom);
            if (Camera != normalized)
            {
                Camera = normalized;
                _demandDirty = true;
                Notify();
            }
        }
        finally
        {
            _entered = false;
        }
    }

    /// <summary>Updates finite logical bounds without starting I/O.</summary>
    /// <param name="logicalSize">Nonnegative logical pixels.</param>
    public void SetViewport(Vector2 logicalSize)
    {
        Enter(true);
        try
        {
            if (!float.IsFinite(logicalSize.X) || !float.IsFinite(logicalSize.Y) || logicalSize.X < 0 || logicalSize.Y < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(logicalSize));
            }

            if (_viewport != logicalSize)
            {
                _viewport = logicalSize;
                _demandDirty = true;
                Notify();
            }
        }
        finally
        {
            _entered = false;
        }
    }

    /// <summary>Collects a bounded number of completions and dispatches within reserved limits.</summary>
    public void Advance()
    {
        Enter(false);
        try
        {
            if (_spatial)
            {
                AdvanceSpatial();
                return;
            }

            int collected = 0;
            bool changed = false;
            foreach (Entry entry in _entries)
            {
                if (entry.Operation is not { IsCompleted: true } operation || collected == _options.MaxPublicationsPerAdvance)
                {
                    continue;
                }

                collected++;
                ResourceReference<MapTileContent>? reference = null;
                ResourceLease<MapTileContent>? lease = null;
                try
                {
                    reference = operation.GetAwaiter().GetResult();
                    if (!_stopping)
                    {
                        lease = reference.Pin();
                        MapTileContent content = lease.Value;
                        if (content.RetainedBytes < 0 || content.RetainedBytes > entry.Charge || string.IsNullOrEmpty(content.SourceRevision))
                        {
                            throw new InvalidDataException("Decoded tile violates its registered reservation or revision contract.");
                        }

                        entry.Reference = reference;
                        entry.Lease = lease;
                        entry.Charge = content.RetainedBytes;
                        reference = null;
                        lease = null;
                        changed = true;
                    }
                }
                catch (ExecutionQueueFullException)
                {
                    entry.Dispatched = false;
                }
                catch (Exception error)
                {
                    entry.Error = error;
                }
                finally
                {
                    lease?.Dispose();
                    reference?.Dispose();
                    entry.Operation = null;
                    EndRetain(entry);
                    if (entry.Lease is null)
                    {
                        entry.Charge = 0;
                    }
                }
            }

            if (!_stopping && _viewport.X > 0 && _viewport.Y > 0)
            {
                foreach (Entry entry in _entries)
                {
                    if (entry.Dispatched || (_options.SelectVisibleTiles && !IsVisible(entry.Id)) || InFlightTiles >= Math.Min(_options.MaxInFlightTiles, _options.MaxCompletedTiles)
                        || ResidentCpuBytes > _options.MaxResidentCpuBytes - _tiles.LoadProfile.MaxReservationBytesPerTile)
                    {
                        continue;
                    }

                    if (!_coordinator.TryBeginRetain())
                    {
                        continue;
                    }

                    entry.CoordinatedRetain = true;
                    entry.Charge = _tiles.LoadProfile.MaxReservationBytesPerTile;
                    entry.Dispatched = true;
                    try
                    {
                        entry.Operation = _resources.RetainAsync(_tiles.GetKey(entry.Id), CancellationToken.None).AsTask();
                    }
                    catch
                    {
                        entry.Charge = 0;
                        entry.Dispatched = false;
                        EndRetain(entry);
                        throw;
                    }
                }
            }

            if (changed)
            {
                Notify();
            }
        }
        finally
        {
            _entered = false;
        }
    }

    /// <summary>Stops dispatch and publication; already dispatched operations remain owned.</summary>
    public void BeginStop()
    {
        Enter(false);
        try
        {
            _stopping = true;
            if (_spatial)
            {
                _entries.RemoveAll(entry => entry.Operation is null && entry.Lease is null && !entry.Retiring);
                foreach (Entry entry in _entries)
                {
                    entry.Wanted = false;
                }
            }
        }
        finally
        {
            _entered = false;
        }
    }

    /// <summary>Stops, drains terminal retains and returns leases; concurrent calls share a retryable attempt.</summary>
    /// <returns>Actual cleanup completion; GPU drain is the caller's prerequisite.</returns>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        if (_disposeTask is { IsCompleted: false })
        {
            return new(_disposeTask);
        }

        if (_entered)
        {
            throw new InvalidOperationException("Session callbacks cannot reenter cleanup.");
        }

        _stopping = true;
        _disposeTask = DisposeCoreAsync();
        return new(_disposeTask);
    }

    private async Task DisposeCoreAsync()
    {
        if (_spatial)
        {
            await DisposeSpatialAsync().ConfigureAwait(false);
            return;
        }

        foreach (Entry entry in _entries)
        {
            if (entry.Operation is { } operation)
            {
                ResourceReference<MapTileContent>? result = null;
                try
                {
                    result = await operation.ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    entry.Error = error;
                }

                result?.Dispose();
                entry.Operation = null;
                EndRetain(entry);
            }

            entry.Lease?.Dispose();
            entry.Lease = null;
            entry.Reference?.Dispose();
            entry.Reference = null;
            entry.Charge = 0;
        }

        _disposed = true;
        Changed = null;
    }

    private void Enter(bool changing)
    {
        EnsureAvailable();
        if (_entered || _disposeTask is { IsCompleted: false } || (changing && _stopping))
        {
            throw new InvalidOperationException("Session operations must be serialized and cannot reenter or change after stop.");
        }

        _entered = true;
    }

    private void EnsureAvailable() => ObjectDisposedException.ThrowIf(_disposed, this);

    private void Notify()
    {
        DisplayRevision++;
        Changed?.Invoke();
    }

    private sealed class Entry(MapTileId id)
    {
        internal MapTileId Id { get; } = id;

        internal MapTileSet? Tiles { get; init; }

        internal bool Wanted { get; set; }

        internal int Priority { get; set; }

        internal double Distance { get; set; }

        internal ulong LastUse { get; set; }

        internal bool Retiring { get; set; }

        internal bool CoordinatedRetain { get; set; }

        internal bool Dispatched { get; set; }

        internal long Charge { get; set; }

        internal Task<ResourceReference<MapTileContent>>? Operation { get; set; }

        internal ResourceReference<MapTileContent>? Reference { get; set; }

        internal ResourceLease<MapTileContent>? Lease { get; set; }

        internal Exception? Error { get; set; }
    }
}
