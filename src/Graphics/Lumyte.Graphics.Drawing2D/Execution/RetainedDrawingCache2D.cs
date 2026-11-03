using System.Numerics;
using System.Runtime.ExceptionServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

internal sealed class RetainedDrawingCache2D : IDisposable
{
    private readonly DrawingRetentionOptions2D _options;
    private readonly List<Asset> _idle = [];
    private readonly List<IDisposable> _retiring = [];
    private readonly object _gate = new();
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
                _retiring.Add(_idle[^1].Batch);
                _idle.RemoveAt(_idle.Count - 1);
            }

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
            return frame;
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
                    _idle.RemoveAt(i);
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
                        _idle.Add(asset);
                    }
                    else
                    {
                        _retiring.Add(asset.Batch);
                    }
                }

                while (_idle.Count > _options.MaxCachedAssets || IdleBytes() > _options.MaxCachedBytes)
                {
                    _retiring.Add(_idle[0].Batch);
                    _idle.RemoveAt(0);
                }

                // At this boundary the caller proved completion or no submission.
                // The cache owns every failed cleanup independently of the frame slot.
                frame.Released = true;
                _frames--;
            }

            DisposeRetiring();
        }
    }

    private void DisposeRetiring()
    {
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

        if (failures is { Count: 1 })
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures is { Count: > 1 })
        {
            throw new AggregateException("Retained resource cleanup failed; the cache owns pending retries.", failures);
        }
    }

    private long IdleBytes()
    {
        long bytes = 0;
        foreach (Asset asset in _idle)
        {
            long count = ((IRetainedCommandBatch2D)asset.Batch).EstimatedBytes;
            bytes = count > long.MaxValue - bytes ? long.MaxValue : bytes + count;
        }

        return bytes;
    }

    internal sealed class FrameLease(RetainedDrawingCache2D owner)
    {
        internal RetainedDrawingCache2D Owner { get; } = owner;

        internal List<Asset> Assets { get; } = [];

        internal bool Released { get; set; }
    }

    internal sealed class Asset(Key key, IPreparedCommandBatch2D batch) : IPreparedCommandBatch2D
    {
        internal Key Key { get; } = key;

        internal IPreparedCommandBatch2D Batch { get; } = batch;

        internal bool Resident { get; set; }

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

    internal sealed record Key(DrawList2D[] Pages, Matrix3x2 Transform, DrawingSceneClip2D[] Clips, uint Width, uint Height, TextureFormat Format, float PixelScale)
    {
        internal bool Matches(Key other)
        {
            if (Transform != other.Transform || Width != other.Width || Height != other.Height || Format != other.Format || PixelScale != other.PixelScale || Pages.Length != other.Pages.Length || Clips.Length != other.Clips.Length)
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
