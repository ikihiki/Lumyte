namespace Lumyte.Graphics.Benchmarks.Testing;

internal abstract class CpuResource : IDisposable
{
    protected CpuResource(CpuGraphicsDevice owner)
    {
        owner.Check();
        Owner = owner;
        Id = ++owner.NextId;
        owner.Live++;
    }

    internal CpuGraphicsDevice Owner
    {
        get;
    }

    internal ulong Id
    {
        get;
    }

    internal bool Disposed
    {
        get;
        private set;
    }

    /// <inheritdoc/>
    public virtual void Dispose()
    {
        if (Disposed)
        {
            return;
        }

        Disposed = true;
        Owner.Live--;
    }

    internal void Check()
    {
        Owner.Check();
        ObjectDisposedException.ThrowIf(Disposed, this);
    }
}
