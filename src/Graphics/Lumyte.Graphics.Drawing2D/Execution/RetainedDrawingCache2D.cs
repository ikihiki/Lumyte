using System.Numerics;
using System.Runtime.ExceptionServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

internal sealed class RetainedDrawingCache2D : IDisposable
{
    private readonly DrawingRetentionOptions2D _options;
    private readonly List<Asset> _idle = [];
    private readonly List<FrameLease> _active = [];
    private readonly List<IDisposable> _retiring = [];
    private readonly object _gate = new();
    private readonly RetainedOwnerLedger2D _ledger = new();
    private Dictionary<object, long>? _memoryOwners = new(ReferenceEqualityComparer.Instance);
    private int _idleVariableAssets;
    private int _activeVariableAssets;
    private DrawingResourceState2D? _storageOwner;
    private int _frames;
    private bool _disposed;
    private long _compiled;
    private long _reused;

    internal RetainedDrawingCache2D(DrawingRetentionOptions2D options)
    {
        options.Validate();
        _options = options;
    }

    internal DrawingRetentionStatistics2D Statistics
    {
        get
        {
            lock (_gate)
            {
                return new(_compiled, _reused, _frames, _idle.Count, IdleBytes());
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            while (_idle.Count != 0)
            {
                Detach(_idle[^1]);
                _retiring.Add(_idle[^1].Batch);
                _idle.RemoveAt(_idle.Count - 1);
            }

            DisposeRetiring();
        }
    }

    internal void BindStorageOwner(DrawingResourceState2D owner)
    {
        lock (_gate)
        {
            if (_storageOwner is not null && !ReferenceEquals(_storageOwner, owner))
            {
                throw new InvalidOperationException("The retained cache already has a different storage owner.");
            }

            _storageOwner = owner;
        }
    }

    internal void RetryCleanup()
    {
        lock (_gate)
        {
            DisposeRetiring();
        }
    }

    internal void CountCompilation() => Interlocked.Increment(ref _compiled);

    internal FrameLease AcquireFrame()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            DisposeRetiring();
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_frames >= _options.MaxFramesInFlight)
            {
                throw new InvalidOperationException("All retained frame slots are busy or quarantined.");
            }

            var frame = new FrameLease(this);
            _frames++;
            _active.Add(frame);
            return frame;
        }
    }

    internal void AdmitFrame(FrameLease frame)
    {
        lock (_gate)
        {
            if (!_active.Contains(frame))
            {
                throw new ArgumentException("The frame is not active in this cache.", nameof(frame));
            }

            while (_idle.Count > 0 && OwnedBytes(includeActive: true) > _options.MaxCachedBytes)
            {
                Detach(_idle[0]);
                _retiring.Add(_idle[0].Batch);
                _idle.RemoveAt(0);
            }

            DisposeRetiring();
        }
    }

    internal Asset? Take(Key key)
    {
        lock (_gate)
        {
            for (int i = _idle.Count - 1; i >= 0; i--)
            {
                if (_idle[i].Key.Matches(key))
                {
                    Asset asset = _idle[i];
                    Detach(asset);
                    _idle.RemoveAt(i);
                    ReleaseUnusedMemoryScratch();
                    _reused++;
                    return asset;
                }
            }

            return null;
        }
    }

    internal void QueueCleanup(IDisposable resource)
    {
        lock (_gate)
        {
            _retiring.Add(resource);
        }
    }

    internal void ReleaseFrame(FrameLease frame, bool completed)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(frame.Owner, this))
            {
                throw new ArgumentException("The frame belongs to another cache.", nameof(frame));
            }

            if (!frame.Released)
            {
                foreach (Asset asset in frame.Assets)
                {
                    // Return state belongs to this frame, not to a reusable asset.
                    // A retry must not touch an asset borrowed by a later frame.
                    asset.Resident |= completed;
                    if (!_disposed && asset.Resident && asset.Batch is IRetainedCommandBatch2D && asset.Batch is not RetainedBatchRun2D { Reusable: false })
                    {
                        ReturnToIdle(asset);
                        _idle.Add(asset);
                    }
                    else
                    {
                        Detach(asset);
                        _retiring.Add(asset.Batch);
                    }
                }

                frame.Released = true;
                _frames--;
                _active.Remove(frame);
                while (_idle.Count > 0 && (_idle.Count > _options.MaxCachedAssets || OwnedBytes(includeActive: true) > _options.MaxCachedBytes))
                {
                    Detach(_idle[0]);
                    _retiring.Add(_idle[0].Batch);
                    _idle.RemoveAt(0);
                }

                // At this boundary the caller proved completion or no submission.
                // The cache owns every failed cleanup independently of the frame slot.
            }

            DisposeRetiring();
        }
    }

    private void DisposeRetiring()
    {
        // Drop traversal capacity before callbacks, including failed cleanup and
        // disposal with active frames whose GPU resources must remain alive.
        ReleaseUnusedMemoryScratch();
        List<Exception>? failures = null;
        for (int index = _retiring.Count - 1; index >= 0; index--)
        {
            try
            {
                _retiring[index].Dispose();
                _retiring.RemoveAt(index);
            }
            catch (Exception failure)
            {
                (failures ??= []).Add(failure);
            }
        }

        long held = OwnedBytes(includeActive: true, includePool: false);
        _storageOwner?.TrimIdleRecordStorage(Math.Max(0, _options.MaxCachedBytes - held));

        if (failures is { Count: 1 })
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures is { Count: > 1 })
        {
            throw new AggregateException("Retained resource cleanup failed; the cache owns pending retries.", failures);
        }
    }

    private void ReleaseUnusedMemoryScratch()
    {
        // Only variable assets need traversal scratch. Empty frame slots and
        // stable ledger-backed assets must not retain its high-water capacity.
        if (_disposed || (_idleVariableAssets == 0 && _activeVariableAssets == 0))
        {
            _memoryOwners = null;
        }
    }

    private long IdleBytes() => OwnedBytes(includeActive: false);

    private void Attach(Asset asset, bool idle)
    {
        if (asset.AccountedIdle is not null)
        {
            return;
        }

        if (asset.Owners is not null || asset.Batch is IRetainedMemory2D { HasStableOwners: true })
        {
            if (asset.Owners is null)
            {
                var owners = new Dictionary<object, long>(ReferenceEqualityComparer.Instance);
                try
                {
                    ((IRetainedMemory2D)asset.Batch).AccountMemory(owners);
                    foreach (DrawList2D page in asset.Key.Pages)
                    {
                        owners.TryAdd(page, checked(256L + ((long)page.Commands.Count * 256)));
                    }

                    asset.Owners = owners.ToArray();
                }
                finally
                {
                    owners.Clear();
                }
            }

            _ledger.Attach(asset.Owners, idle);
        }
        else if (idle)
        {
            _idleVariableAssets++;
        }
        else
        {
            _activeVariableAssets++;
        }

        asset.AccountedIdle = idle;
    }

    private void ReturnToIdle(Asset asset)
    {
        if (asset.AccountedIdle != false)
        {
            throw new InvalidOperationException("Only active assets can return to idle.");
        }

        if (asset.Owners is not null)
        {
            _ledger.ReturnToIdle(asset.Owners);
        }
        else
        {
            _activeVariableAssets--;
            _idleVariableAssets++;
        }

        asset.AccountedIdle = true;
    }

    private void Detach(Asset asset)
    {
        if (asset.AccountedIdle is not { } idle)
        {
            return;
        }

        if (asset.Owners is not null)
        {
            _ledger.Detach(asset.Owners, idle);
        }
        else if (idle)
        {
            _idleVariableAssets--;
        }
        else
        {
            _activeVariableAssets--;
        }

        asset.AccountedIdle = null;
    }

    private long OwnedBytes(bool includeActive, bool includePool = true)
    {
        bool variable = _idleVariableAssets != 0 || (includeActive && _activeVariableAssets != 0);

        if (variable)
        {
            return ScanOwnedBytes(includeActive, includePool);
        }

        long bytes = _ledger.Bytes(includeActive);
        long pool = includePool ? _storageOwner?.IdleRecordBytes ?? 0 : 0;
        return pool > long.MaxValue - bytes ? long.MaxValue : bytes + pool;
    }

    private long ScanOwnedBytes(bool includeActive, bool includePool)
    {
        // Reuse traversal storage, never estimates: plans and pooled capacity can change.
        Dictionary<object, long> owners = _memoryOwners ?? new(ReferenceEqualityComparer.Instance);
        _memoryOwners = null;
        try
        {
            foreach (Asset asset in _idle)
            {
                Account(asset);
            }

            if (includeActive)
            {
                foreach (FrameLease frame in _active)
                {
                    foreach (Asset asset in frame.Assets)
                    {
                        Account(asset);
                    }
                }
            }

            long bytes = includePool ? _storageOwner?.IdleRecordBytes ?? 0 : 0;
            foreach (long count in owners.Values)
            {
                bytes = count > long.MaxValue - bytes ? long.MaxValue : bytes + count;
            }

            return bytes;
        }
        finally
        {
            // An observation must not keep pages or shared plans alive, even on failure.
            owners.Clear();

            // Dictionary arrays are traversal scratch, not cached drawing bytes.
            // Reentrant transitions may have removed the last variable asset.
            _memoryOwners = owners;
            ReleaseUnusedMemoryScratch();
        }

        void Account(Asset asset)
        {
            if (asset.Batch is IRetainedMemory2D shared)
            {
                shared.AccountMemory(owners);
            }
            else if (asset.Batch is IRetainedCommandBatch2D retained)
            {
                owners.TryAdd(asset.Batch, retained.EstimatedBytes);
            }

            foreach (DrawList2D page in asset.Key.Pages)
            {
                owners.TryAdd(page, checked(256L + ((long)page.Commands.Count * 256)));
            }
        }
    }

    internal sealed class FrameLease
    {
        internal FrameLease(RetainedDrawingCache2D owner)
        {
            Owner = owner;
            Assets = new(this);
        }

        internal RetainedDrawingCache2D Owner { get; }

        internal AssetList Assets { get; }

        internal bool Released { get; set; }
    }

    internal sealed class AssetList(FrameLease frame) : System.Collections.ObjectModel.Collection<Asset>
    {
        public new List<Asset>.Enumerator GetEnumerator() => ((List<Asset>)Items).GetEnumerator();

        protected override void InsertItem(int index, Asset item)
        {
            lock (frame.Owner._gate)
            {
                if (frame.Released)
                {
                    throw new InvalidOperationException("Cannot add assets to a released frame.");
                }

                if (item.AccountedIdle is not null)
                {
                    throw new InvalidOperationException("The asset already belongs to a cache scope.");
                }

                if ((uint)index > (uint)Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                frame.Owner.Attach(item, idle: false);
                try
                {
                    if (frame.Released)
                    {
                        throw new InvalidOperationException("Accounting released this frame reentrantly.");
                    }

                    base.InsertItem(index, item);
                }
                catch
                {
                    frame.Owner.Detach(item);
                    throw;
                }
            }
        }

        protected override void RemoveItem(int index) => throw new NotSupportedException();

        protected override void SetItem(int index, Asset item) => throw new NotSupportedException();

        protected override void ClearItems() => throw new NotSupportedException();
    }

    internal sealed class Asset(Key key, IPreparedCommandBatch2D batch) : IPreparedCommandBatch2D
    {
        internal Key Key { get; } = key;

        internal IPreparedCommandBatch2D Batch { get; } = batch;

        internal bool Resident { get; set; }

        internal KeyValuePair<object, long>[]? Owners { get; set; }

        internal bool? AccountedIdle { get; set; }

        public void DeclareResources(IDrawingResourceDeclarations2D resources) => Batch.DeclareResources(resources);

        public void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target)
        {
            if (Batch is IRetainedCommandBatch2D retained)
            {
                retained.RecordRetained(commands, target, Resident);
            }
            else
            {
                Batch.Record(commands, target);
            }
        }

        public void Dispose() => throw new InvalidOperationException("Compiled assets are owned by the frame cache.");
    }

    internal sealed record Key(DrawList2D[] Pages, Matrix3x2 Transform, DrawingSceneClip2D[] Clips, uint Width, uint Height, TextureFormat Format, float PixelScale, int FirstCommand = 0, int CommandCount = int.MaxValue)
    {
        internal bool Matches(Key other)
        {
            if (FirstCommand != other.FirstCommand || CommandCount != other.CommandCount || Transform != other.Transform || Width != other.Width || Height != other.Height || Format != other.Format || PixelScale != other.PixelScale || Pages.Length != other.Pages.Length || Clips.Length != other.Clips.Length)
            {
                return false;
            }

            for (int i = 0; i < Pages.Length; i++)
            {
                if (!ReferenceEquals(Pages[i], other.Pages[i]))
                {
                    return false;
                }
            }

            for (int i = 0; i < Clips.Length; i++)
            {
                if (Clips[i] != other.Clips[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
