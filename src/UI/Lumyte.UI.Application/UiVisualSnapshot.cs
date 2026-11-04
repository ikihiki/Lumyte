using Lumyte.Graphics.Abstractions;

namespace Lumyte.UI.Application;

internal sealed class UiVisualSnapshot(IGraphicsTexture texture, IGraphicsTextureView view, IDisposable reservation) : IDisposable
{
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _references = 1;
    private int _ownerReleased;

    internal Task Released => _released.Task;

    internal IGraphicsTextureView View => view;

    internal uint Width => texture.Width;

    internal uint Height => texture.Height;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _ownerReleased, 1) == 0)
        {
            Release();
        }
    }

    internal IDisposable Retain()
    {
        int count = Volatile.Read(ref _references);
        while (true)
        {
            ObjectDisposedException.ThrowIf(count <= 0, this);
            int observed = Interlocked.CompareExchange(ref _references, checked(count + 1), count);
            if (observed == count)
            {
                return new Lease(this);
            }

            count = observed;
        }
    }

    private void Release()
    {
        if (Interlocked.Decrement(ref _references) == 0)
        {
            try
            {
                view.Dispose();
                texture.Dispose();
                reservation.Dispose();
                _released.TrySetResult();
            }
            catch (Exception error)
            {
                _released.TrySetException(error);
                throw;
            }
        }
    }

    private sealed class Lease(UiVisualSnapshot owner) : IDisposable
    {
        private UiVisualSnapshot? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }
}
