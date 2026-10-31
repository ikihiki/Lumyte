namespace Lumyte.Signals;

/// <summary>Optionally groups observers and asynchronous operations without owning shared signal values.</summary>
public sealed class SignalScope : IDisposable
{
    private readonly List<IDisposable> _resources = [];
    private bool _disposed;
    private int _running;

    /// <summary>Captures observers and asynchronous signals created in a synchronous setup block, even when setup throws.</summary>
    /// <param name="setup">The synchronous setup block.</param>
    public void Run(Action setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        SignalContext context = SignalContext.Current;
        SignalScope? previous;
        lock (SignalContext.Gate)
        {
            SignalContext.CheckMutation();
            ObjectDisposedException.ThrowIf(_disposed, this);
            previous = context.Scope;
            context.Scope = this;
            _running++;
        }

        try
        {
            setup();
        }
        finally
        {
            lock (SignalContext.Gate)
            {
                _running--;
                context.Scope = previous;
            }
        }
    }

    /// <summary>Disposes captured resources in reverse order, leaving shared values usable.</summary>
    public void Dispose()
    {
        IDisposable[] resources;
        lock (SignalContext.Gate)
        {
            SignalContext.CheckMutation();
            if (_disposed)
            {
                return;
            }

            if (_running != 0)
            {
                throw new InvalidOperationException("A running scope cannot be disposed.");
            }

            _disposed = true;
            resources = _resources.ToArray();
            _resources.Clear();
        }

        List<Exception>? failures = null;
        for (int index = resources.Length - 1; index >= 0; index--)
        {
            try
            {
                resources[index].Dispose();
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException(failures);
        }
    }

    internal void Add(IDisposable resource) => _resources.Add(resource);
}
