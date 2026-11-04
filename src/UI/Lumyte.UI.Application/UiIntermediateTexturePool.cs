using Lumyte.Graphics.Abstractions;

namespace Lumyte.UI.Application;

/// <summary>Retains a bounded set of completed intermediate targets within one presenter/device generation.</summary>
internal sealed class UiIntermediateTexturePool(Func<uint, uint, IGraphicsTexture> create) : IDisposable
{
    private const int MaximumIdleCount = 2;
    private const ulong MaximumIdleBytes = 64UL * 1024 * 1024;
    private readonly List<Entry> _idle = [];
    private readonly object _sync = new();
    private ulong _idleBytes;
    private bool _disposed;
    private bool _failed;

    internal int IdleCount
    {
        get
        {
            lock (_sync)
            {
                return _idle.Count;
            }
        }
    }

    internal ulong IdleBytes
    {
        get
        {
            lock (_sync)
            {
                return _idleBytes;
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            while (_idle.Count != 0)
            {
                EvictFirst();
            }
        }
    }

    internal void StopRenting()
    {
        lock (_sync)
        {
            _disposed = true;
        }
    }

    internal Entry Rent(uint width, uint height, TextureFormat format)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_failed)
            {
                throw new InvalidOperationException("Intermediate resource cleanup failed.");
            }

            for (int index = 0; index < _idle.Count; index++)
            {
                Entry entry = _idle[index];
                if (entry.Texture.Width == width && entry.Texture.Height == height && entry.Texture.Format == format)
                {
                    _idle.RemoveAt(index);
                    _idleBytes -= entry.Bytes;
                    entry.Leased = true;
                    return entry;
                }
            }

            IGraphicsTexture texture = create(width, height);
            try
            {
                return new(this, texture, texture.CreateView());
            }
            catch
            {
                texture.Dispose();
                throw;
            }
        }
    }

    private static ulong Capacity(IGraphicsTexture texture)
    {
        ulong texels = (ulong)texture.Width * texture.Height;
        ulong bytesPerTexel = texture.Format == TextureFormat.Rgba16Float ? 8UL : 4UL;
        return texels > MaximumIdleBytes / bytesPerTexel ? MaximumIdleBytes + 1 : texels * bytesPerTexel;
    }

    private void Return(Entry entry)
    {
        lock (_sync)
        {
            if (!entry.Leased)
            {
                return;
            }

            if (_disposed || _failed || entry.Discarded || entry.Bytes > MaximumIdleBytes)
            {
                entry.Dispose();
            }
            else
            {
                while (_idle.Count >= MaximumIdleCount || _idleBytes + entry.Bytes > MaximumIdleBytes)
                {
                    EvictFirst();
                }

                _idle.Add(entry);
                _idleBytes += entry.Bytes;
            }

            entry.Leased = false;
        }
    }

    private void EvictFirst()
    {
        Entry entry = _idle[0];
        try
        {
            entry.Dispose();
        }
        catch
        {
            _failed = true;
            throw;
        }

        _idle.RemoveAt(0);
        _idleBytes -= entry.Bytes;
    }

    internal sealed class Entry(UiIntermediateTexturePool pool, IGraphicsTexture texture, IGraphicsTextureView view) : IDisposable
    {
        private bool _viewDisposed;
        private bool _textureDisposed;

        internal IGraphicsTexture Texture { get; } = texture;

        internal IGraphicsTextureView View { get; } = view;

        internal ulong Bytes { get; } = Capacity(texture);

        internal bool Leased { get; set; } = true;

        internal bool Discarded { get; private set; }

        internal TextureState State { get; set; }

        internal TextureBarrierDesc ColorAttachmentBarrier => new()
        {
            Texture = Texture,
            Range = new(0, 1, 0, 1),
            BeforeState = State,
            AfterState = TextureState.ColorAttachment,
            Before = State switch
            {
                TextureState.Sampled => new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead),
                TextureState.CopySource => new(PipelineStage.Copy, ResourceAccess.CopyRead),
                _ => default,
            },
            After = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite),
        };

        public void Dispose()
        {
            lock (pool._sync)
            {
                Discarded = true;
                if (!_viewDisposed)
                {
                    View.Dispose();
                    _viewDisposed = true;
                }

                if (!_textureDisposed)
                {
                    Texture.Dispose();
                    _textureDisposed = true;
                }
            }
        }

        internal void Discard()
        {
            lock (pool._sync)
            {
                Discarded = true;
            }
        }

        internal void Release() => pool.Return(this);
    }
}
