using Lumyte.Execution;

namespace Lumyte.Maps;

/// <summary>Owns bounded spatial retains, LRU retirement and asynchronous shared-manager maintenance.</summary>
public sealed partial class MapSession
{
    private readonly object _maintenanceOwner = new();
    private Task<int>? _maintenance;
    private Entry[] _maintenanceBatch = [];
    private bool _maintenanceRequired;
    private Exception? _maintenanceFailure;
    private bool _displayDirty;

    /// <summary>Gets the number of retired tile charges awaiting actual manager cleanup.</summary>
    public int RetiringTiles => _entries.Count(entry => entry.Retiring);

    /// <summary>Gets a value indicating whether this session owns an unfinished manager maintenance operation.</summary>
    public bool IsMaintenanceInFlight => _maintenance is not null;

    /// <summary>Gets the last maintenance failure; charges remain held until a successful retry.</summary>
    public Exception? MaintenanceFailure => _maintenanceFailure;

    /// <summary>Gets actual shared-manager generation releases observed by this session's successful maintenance sweeps.</summary>
    public ulong ReleasedGenerationCount { get; private set; }

    /// <summary>Gets adopted tiles at the current source zoom intersecting the current viewport.</summary>
    public int ReadyVisibleTiles => _entries.Count(entry => entry.Lease is not null && !entry.Retiring
        && (!_spatial || IsCurrent(entry)) && entry.Id.Zoom == SourceZoom && IsVisible(entry.Id));

    private bool SpatialStopped => _stopping && _maintenance is null && !_maintenanceRequired && _maintenanceFailure is null
        && _entries.All(entry => entry.Operation is null && entry.Lease is null && entry.Reference is null && !entry.Retiring);

    private void AdvanceSpatial()
    {
        IsCapacityBlocked = false;
        _displayDirty = false;
        CollectMaintenance();
        if (_demandDirty && !_stopping)
        {
            ReconcileDemand();
        }

        bool changed = false;
        int collected = 0;
        foreach (Entry entry in _entries)
        {
            if (entry.Operation is not { IsCompleted: true } operation || collected == _options.MaxPublicationsPerAdvance)
            {
                continue;
            }

            collected++;
            try
            {
                entry.Reference = operation.GetAwaiter().GetResult();
                if (!_stopping && entry.Wanted && IsCurrent(entry))
                {
                    entry.Lease = entry.Reference.Pin();
                    MapTileContent content = entry.Lease.Value;
                    string? expected = _tiles.GetExpectedSourceRevision(entry.Id);
                    if (content.RetainedBytes < 0 || content.RetainedBytes > entry.Charge
                        || string.IsNullOrEmpty(content.SourceRevision) || (expected is not null && expected != content.SourceRevision))
                    {
                        throw new InvalidDataException("Completed tile violates its reservation or expected source revision.");
                    }

                    entry.Charge = content.RetainedBytes;
                    entry.LastUse = ++_useSequence;
                    changed = true;
                    _demandDirty = true;
                }
                else
                {
                    Retire(entry);
                }
            }
            catch (ExecutionQueueFullException)
            {
                entry.Dispatched = false;
                Retire(entry);
            }
            catch (Exception error)
            {
                entry.Error = error;
                Retire(entry);
            }
            finally
            {
                entry.Operation = null;
                EndRetain(entry);
            }
        }

        if (_demandDirty && !_stopping)
        {
            ReconcileDemand();
        }

        foreach (Entry entry in _entries)
        {
            if (entry.Operation is null && !entry.Retiring && entry.Lease is not null && (_stopping || !IsCurrent(entry)))
            {
                Retire(entry);
                changed = true;
            }
        }

        if (_stopping)
        {
            _entries.RemoveAll(entry => entry.Operation is null && !entry.Retiring && entry.Lease is null);
        }
        else if (_maintenance is null && !_maintenanceRequired && _maintenanceFailure is null)
        {
            foreach (Entry entry in _entries.Where(entry => entry.Wanted && !entry.Dispatched && !entry.Retiring)
                .OrderBy(entry => entry.Priority).ThenBy(entry => entry.Distance).ThenBy(entry => entry.Id.Zoom)
                .ThenBy(entry => entry.Id.X).ThenBy(entry => entry.Id.Y).ToArray())
            {
                if (InFlightTiles >= Math.Min(_options.MaxInFlightTiles, _options.MaxCompletedTiles))
                {
                    break;
                }

                if (!MakeRoom(entry))
                {
                    IsCapacityBlocked = true;
                    break;
                }

                if (!_coordinator.TryBeginRetain())
                {
                    IsCapacityBlocked = true;
                    break;
                }

                entry.CoordinatedRetain = true;
                entry.Charge = _tiles.LoadProfile.MaxReservationBytesPerTile;
                entry.Dispatched = true;
                try
                {
                    entry.Operation = _resources.RetainAsync(_tiles.GetKey(entry.Id), CancellationToken.None).AsTask();
                }
                catch (Exception error)
                {
                    entry.Error = error;
                    EndRetain(entry);
                    Retire(entry);
                    break;
                }
            }
        }
        else
        {
            IsCapacityBlocked = PendingTiles != 0;
        }

        if (_maintenanceRequired && _maintenance is null && _coordinator.TryBeginTrim())
        {
            _maintenanceBatch = _entries.Where(entry => entry.Retiring).ToArray();
            _maintenance = _resources.TrimAsync(CancellationToken.None).AsTask();
        }

        if ((changed || _displayDirty) && !_stopping)
        {
            Notify();
        }
    }

    private void EndRetain(Entry entry)
    {
        if (entry.CoordinatedRetain)
        {
            entry.CoordinatedRetain = false;
            _coordinator.EndRetain();
        }
    }

    private bool MakeRoom(Entry incoming)
    {
        long reservation = _tiles.LoadProfile.MaxReservationBytesPerTile;
        int slots = _entries.Count(entry => entry.Operation is not null || entry.Lease is not null || entry.Retiring);
        if (slots < _options.MaxResidentTiles && ResidentCpuBytes <= _options.MaxResidentCpuBytes - reservation)
        {
            return true;
        }

        bool canReplaceFallback = incoming.Priority == 0 && InFlightTiles == 0;
        Entry? victim = _entries.Where(entry => entry.Lease is not null && !entry.Retiring
                && (!entry.Wanted || (canReplaceFallback && entry.Priority > 0)))
            .OrderBy(entry => entry.LastUse).ThenBy(entry => entry.Id.Zoom).ThenBy(entry => entry.Id.X).ThenBy(entry => entry.Id.Y).FirstOrDefault();
        if (victim is not null)
        {
            Retire(victim);
        }

        // Build-view pins must drain through the shared Maps barrier before unused generations can be reclaimed.
        return false;
    }

    private void Retire(Entry entry)
    {
        _displayDirty |= entry.Lease is not null;
        entry.Lease?.Dispose();
        entry.Lease = null;
        entry.Reference?.Dispose();
        entry.Reference = null;
        entry.Retiring = true;
        _maintenanceRequired = true;
        _coordinator.Request(_maintenanceOwner);
        _demandDirty = true;
    }

    private void CollectMaintenance()
    {
        if (_maintenance is not { IsCompleted: true } operation)
        {
            return;
        }

        try
        {
            ReleasedGenerationCount += (ulong)operation.GetAwaiter().GetResult();
            foreach (Entry entry in _maintenanceBatch)
            {
                entry.Charge = 0;
                entry.Retiring = false;
            }

            _entries.RemoveAll(entry => !entry.Retiring && entry.Lease is null && entry.Operation is null && (entry.Error is null || !entry.Wanted || _stopping));
            _maintenanceRequired = _entries.Any(entry => entry.Retiring);
            _maintenanceFailure = null;
            _demandDirty = true;
        }
        catch (Exception error)
        {
            _maintenanceFailure = error;
        }
        finally
        {
            _maintenance = null;
            _maintenanceBatch = [];
            _coordinator.EndTrim(_maintenanceOwner, _maintenanceRequired);
        }
    }

    private async Task DisposeSpatialAsync()
    {
        foreach (Entry entry in _entries)
        {
            if (entry.Operation is { } operation)
            {
                try
                {
                    entry.Reference = await operation.ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    entry.Error = error;
                }

                entry.Operation = null;
                EndRetain(entry);
            }

            Retire(entry);
        }

        if (_maintenance is { } previous)
        {
            try
            {
                _ = await previous.ConfigureAwait(false);
            }
            catch (Exception error)
            {
                _maintenanceFailure = error;
            }

            _maintenance = null;
            _coordinator.EndTrim(_maintenanceOwner, true);
        }

        // Always perform a final sweep after terminal references return, including shared Manager cache hits.
        _maintenanceRequired = true;
        await _coordinator.BeginTrimAsync(_maintenanceOwner).ConfigureAwait(false);
        _maintenance = _resources.TrimAsync(CancellationToken.None).AsTask();
        try
        {
            _ = await _maintenance.ConfigureAwait(false);
        }
        catch (Exception error)
        {
            _maintenanceFailure = error;
            _maintenance = null;
            _coordinator.EndTrim(_maintenanceOwner, true);
            throw;
        }

        _maintenance = null;
        _maintenanceRequired = false;
        _maintenanceFailure = null;
        _coordinator.EndTrim(_maintenanceOwner, false);
        _entries.Clear();
        _maintenanceBatch = [];
        _disposed = true;
        Changed = null;
    }
}
