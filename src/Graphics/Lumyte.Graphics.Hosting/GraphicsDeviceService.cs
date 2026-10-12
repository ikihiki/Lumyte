using System.Diagnostics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Hosting;

internal sealed class GraphicsDeviceService : IGraphicsDeviceService
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _cleanup = new(1);
    private readonly IGraphicsDeviceFactory _factory;
    private readonly List<Entry> _entries = [];
    private GraphicsDeviceState _state;
    private Entry? _current;
    private Task<GraphicsDeviceState>? _operation;
    private bool _closing;

    internal GraphicsDeviceService(string name, IGraphicsDeviceFactory factory)
    {
        _factory = factory;
        _state = new() { Name = name, Version = 0, Epoch = 0, Status = GraphicsDeviceServiceStatus.Uninitialized, InvalidatedThroughVersion = 0 };
    }

    public event EventHandler? StateChanged;

    public GraphicsDeviceState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
        => await StartCreation(false, cancellationToken).WaitAsync(cancellationToken);

    public ValueTask<GraphicsDeviceLease> AcquireAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ValidateOpen();
            if (_state.Status != GraphicsDeviceServiceStatus.Ready || _current is null || _current.Owner.Device.Status != GraphicsDeviceStatus.Ready)
            {
                throw new GraphicsDeviceUnavailableException(_state);
            }

            Entry entry = _current;
            entry.Leases = checked(entry.Leases + 1);
            return ValueTask.FromResult(new GraphicsDeviceLease(entry.Owner.Device, _state, () => Release(entry)));
        }
    }

    public async ValueTask<GraphicsDeviceState> RecoverAsync(CancellationToken cancellationToken = default)
        => await StartCreation(true, cancellationToken).WaitAsync(cancellationToken);

    public ValueTask WaitForRetirementAsync(ulong epoch, CancellationToken cancellationToken = default)
    {
        Entry entry;
        lock (_gate)
        {
            ValidateOpen();
            entry = _entries.Find(x => x.Epoch == epoch) ?? throw new ArgumentException("Unknown retained epoch.", nameof(epoch));
            if (entry.Owner.Device.Status != GraphicsDeviceStatus.Lost || entry.Cleaning)
            {
                throw new InvalidOperationException("Epoch is not available for loss retirement confirmation.");
            }

            entry.Leases = checked(entry.Leases + 1);
        }

        return ConfirmAsync(entry, cancellationToken);
    }

    public async ValueTask<int> RetireAsync(CancellationToken cancellationToken = default)
    {
        await _cleanup.WaitAsync(cancellationToken);
        try
        {
            lock (_gate)
            {
                ValidateOpen();
            }

            return await CleanupAsync(false, cancellationToken);
        }
        finally
        {
            _cleanup.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cleanup.WaitAsync();
        try
        {
            lock (_gate)
            {
                if (_state.Status == GraphicsDeviceServiceStatus.Disposed)
                {
                    return;
                }

                if (_operation is { IsCompleted: false } || _entries.Any(x => x.Leases != 0))
                {
                    throw new InvalidOperationException("Finish creation and return all epoch leases before disposing the graphics scope.");
                }

                _closing = true;
            }

            await CleanupAsync(true, CancellationToken.None);
            lock (_gate)
            {
                ChangeState(GraphicsDeviceServiceStatus.Disposed);
            }

            Notify();
        }
        finally
        {
            _cleanup.Release();
        }
    }

    private Task<GraphicsDeviceState> StartCreation(bool recovery, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TaskCompletionSource<GraphicsDeviceState> completion;
        lock (_gate)
        {
            ValidateOpen();
            if (_operation is { IsCompleted: false })
            {
                return _operation;
            }

            if (_state.Status == GraphicsDeviceServiceStatus.Ready)
            {
                return Task.FromResult(_state);
            }

            if (!recovery && _state.Status != GraphicsDeviceServiceStatus.Uninitialized)
            {
                throw new GraphicsDeviceUnavailableException(_state);
            }

            if (recovery && _state.Status is not (GraphicsDeviceServiceStatus.Lost or GraphicsDeviceServiceStatus.Failed))
            {
                throw new GraphicsDeviceUnavailableException(_state);
            }

            // Reserve enough counter space for the start, completion and a concurrent loss.
            if (_state.Version > ulong.MaxValue - 3 || _state.Epoch == ulong.MaxValue)
            {
                throw new InvalidOperationException("Graphics generation counters are exhausted.");
            }

            _current = null;
            ChangeState(recovery ? GraphicsDeviceServiceStatus.Recovering : GraphicsDeviceServiceStatus.Initializing);
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _operation = completion.Task;
        }

        Notify();
        _ = CreateAsync(completion);
        return completion.Task;
    }

    private async Task CreateAsync(TaskCompletionSource<GraphicsDeviceState> completion)
    {
        try
        {
            IGraphicsDeviceOwner owner = await _factory.CreateAsync();
            ArgumentNullException.ThrowIfNull(owner);
            ArgumentNullException.ThrowIfNull(owner.Device);
            Entry entry;
            lock (_gate)
            {
                ulong epoch = checked(_state.Epoch + 1);
                entry = new(owner, epoch);
                entry.Handler = (_, _) => OnLost(entry);
                _entries.Add(entry);
                _current = entry;
                _state = _state with { Epoch = epoch };
            }

            owner.Device.DeviceLost += entry.Handler;
            lock (_gate)
            {
                if (owner.Device.Status != GraphicsDeviceStatus.Ready)
                {
                    ChangeState(GraphicsDeviceServiceStatus.Failed);
                    throw new GraphicsDeviceUnavailableException(_state);
                }

                ChangeState(GraphicsDeviceServiceStatus.Ready);
                entry.ReadyVersion = _state.Version;
            }

            // Catch a loss racing with subscription or the Ready publication.
            if (owner.Device.Status == GraphicsDeviceStatus.Lost)
            {
                OnLost(entry);
            }

            Notify();
            GraphicsDeviceState state = State;
            if (state.Status != GraphicsDeviceServiceStatus.Ready)
            {
                throw new GraphicsDeviceUnavailableException(state);
            }

            completion.TrySetResult(state);
        }
        catch (Exception error)
        {
            lock (_gate)
            {
                if (_state.Status is GraphicsDeviceServiceStatus.Initializing or GraphicsDeviceServiceStatus.Recovering)
                {
                    ChangeState(GraphicsDeviceServiceStatus.Failed);
                }
            }

            Notify();
            completion.TrySetException(error);
        }
    }

    private void OnLost(Entry entry)
    {
        bool changed = false;
        lock (_gate)
        {
            if (ReferenceEquals(entry, _current) && _state.Status == GraphicsDeviceServiceStatus.Ready)
            {
                _state = _state with { InvalidatedThroughVersion = Math.Max(_state.InvalidatedThroughVersion, entry.ReadyVersion) };
                ChangeState(GraphicsDeviceServiceStatus.Lost);
                changed = true;
            }
        }

        if (changed)
        {
            Notify();
        }
    }

    private async ValueTask ConfirmAsync(Entry entry, CancellationToken cancellationToken)
    {
        try
        {
            await entry.Owner.Device.WaitForRetirementAsync(cancellationToken);
        }
        finally
        {
            Release(entry);
        }
    }

    private async ValueTask<int> CleanupAsync(bool all, CancellationToken cancellationToken)
    {
        Entry[] candidates;
        lock (_gate)
        {
            candidates = _entries.Where(x => x.Leases == 0 && (all || !ReferenceEquals(x, _current) || _state.Status is GraphicsDeviceServiceStatus.Lost or GraphicsDeviceServiceStatus.Failed)).ToArray();
            foreach (Entry entry in candidates)
            {
                entry.Cleaning = true;
            }
        }

        int released = 0;
        try
        {
            foreach (Entry entry in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Owner.Device.Status == GraphicsDeviceStatus.Lost)
                {
                    await entry.Owner.Device.WaitForRetirementAsync(cancellationToken);
                }

                await entry.Owner.DisposeAsync();
                entry.Owner.Device.DeviceLost -= entry.Handler;
                lock (_gate)
                {
                    _entries.Remove(entry);
                    if (ReferenceEquals(_current, entry))
                    {
                        _current = null;
                    }
                }

                released++;
            }
        }
        finally
        {
            lock (_gate)
            {
                foreach (Entry entry in candidates)
                {
                    entry.Cleaning = false;
                }
            }
        }

        return released;
    }

    private void Release(Entry entry)
    {
        lock (_gate)
        {
            entry.Leases--;
        }
    }

    private void ChangeState(GraphicsDeviceServiceStatus status) => _state = _state with { Status = status, Version = checked(_state.Version + 1) };

    private void ValidateOpen() => ObjectDisposedException.ThrowIf(_closing || _state.Status == GraphicsDeviceServiceStatus.Disposed, this);

    private void Notify()
    {
        foreach (Delegate handler in StateChanged?.GetInvocationList() ?? [])
        {
            try
            {
                ((EventHandler)handler)(this, EventArgs.Empty);
            }
            catch (Exception error)
            {
                Trace.TraceError("Graphics state subscriber failed: {0}", error);
            }
        }
    }

    private sealed class Entry(IGraphicsDeviceOwner owner, ulong epoch)
    {
        internal IGraphicsDeviceOwner Owner { get; } = owner;

        internal ulong Epoch { get; } = epoch;

        internal ulong ReadyVersion { get; set; }

        internal int Leases { get; set; }

        internal bool Cleaning { get; set; }

        internal EventHandler<GraphicsDeviceLostEventArgs>? Handler { get; set; }
    }
}
