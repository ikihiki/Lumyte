using System.Diagnostics;
using Lumyte.Audio.Abstractions;
using Lumyte.Execution;

namespace Lumyte.Audio.Execution;

/// <summary>Owns device generations; all native access is performed by generated owner commands.</summary>
internal sealed class AudioDeviceState(IAudioDeviceFactory factory) : IAudioDeviceService
{
    private readonly object _gate = new();
    private readonly List<AudioDeviceEntry> _entries = [];
    private AudioDeviceSnapshot _state;
    private AudioDeviceEntry? _current;
    private Task<IAudioDevice>? _pending;
    private Task<AudioDeviceSnapshot>? _recovery;
    private bool _closing;
    private int _ownerThreadId;

    public event EventHandler? StateChanged;

    public AudioDeviceSnapshot State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public int OwnerThreadId => Volatile.Read(ref _ownerThreadId);

    internal Task<IAudioDevice>? PendingCreation
    {
        get
        {
            lock (_gate)
            {
                return _pending;
            }
        }
    }

    internal Task RecoveryCompletion
    {
        get
        {
            lock (_gate)
            {
                return _recovery ?? Task.CompletedTask;
            }
        }
    }

    public ValueTask<AudioDeviceLease> AcquireAsync(CancellationToken cancellationToken = default)
        => new AudioDevice(this).AcquireLeaseAsync(cancellationToken);

    public ValueTask<AudioDeviceSnapshot> RecoverAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TaskCompletionSource<AudioDeviceSnapshot>? start = null;
        Task<AudioDeviceSnapshot> operation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            if (_recovery is not { IsCompleted: false })
            {
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _recovery = start.Task;
            }

            operation = _recovery;
        }

        if (start is not null)
        {
            _ = RecoverCoreAsync(start);
        }

        return new(operation.WaitAsync(cancellationToken));
    }

    public ValueTask<int> RetireAsync(CancellationToken cancellationToken = default)
        => new AudioDevice(this).RetireAsync(cancellationToken);

    internal AudioDeviceSnapshot? BeginRecovery()
    {
        VerifyOwner();
        PollAvailability();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            if (_state.Status == AudioDeviceServiceStatus.Ready)
            {
                return _state;
            }
        }

        BeginCreate(true);
        return null;
    }

    internal Task<IAudioDevice> BeginCreate(bool recovery)
    {
        VerifyOwner(recovery);
        if (recovery)
        {
            PollAvailability();
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            if (recovery && _pending is not null && _state.Status == AudioDeviceServiceStatus.Recovering)
            {
                return _pending;
            }

            if (_pending is not null || (recovery
                ? _state.Status is not (AudioDeviceServiceStatus.Lost or AudioDeviceServiceStatus.Failed)
                : _state.Status != AudioDeviceServiceStatus.Uninitialized))
            {
                throw new InvalidOperationException("Audio device creation is not valid in the current state.");
            }

            ChangeState(recovery ? AudioDeviceServiceStatus.Recovering : AudioDeviceServiceStatus.Initializing);
        }

        Notify();
        try
        {
            Task<IAudioDevice> pending = factory.CreateAsync();
            lock (_gate)
            {
                _pending = pending;
            }

            return pending;
        }
        catch
        {
            lock (_gate)
            {
                ChangeState(AudioDeviceServiceStatus.Failed);
            }

            Notify();
            throw;
        }
    }

    internal AudioDeviceSnapshot CompleteCreate()
    {
        VerifyOwner();
        Task<IAudioDevice> pending;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            pending = _pending ?? throw new InvalidOperationException("Audio creation is not pending.");
            if (!pending.IsCompleted)
            {
                throw new InvalidOperationException("Audio creation has not completed.");
            }
        }

        IAudioDevice device;
        try
        {
            device = pending.GetAwaiter().GetResult();
        }
        catch
        {
            lock (_gate)
            {
                _pending = null;
                ChangeState(AudioDeviceServiceStatus.Failed);
            }

            Notify();
            throw;
        }

        AudioDeviceEntry entry;
        lock (_gate)
        {
            entry = new(device, checked(_state.Epoch + 1));
            _entries.Add(entry);
            _current = entry;
            _pending = null;
            _state = _state with { Epoch = entry.Epoch };
        }

        try
        {
            if (device.OutputState == AudioOutputState.Unavailable)
            {
                throw new AudioDeviceUnavailableException("The created audio device is unavailable.");
            }

            lock (_gate)
            {
                ChangeState(AudioDeviceServiceStatus.Ready);
                entry.ReadyVersion = _state.Version;
            }
        }
        catch
        {
            lock (_gate)
            {
                entry.Lost = true;
                ChangeState(AudioDeviceServiceStatus.Failed);
            }

            Notify();
            throw;
        }

        AudioDeviceSnapshot ready = State;
        Notify();
        return ready;
    }

    internal AudioDeviceLease AcquireLease()
    {
        VerifyOwner();
        PollAvailability();
        lock (_gate)
        {
            AudioDeviceEntry entry = RequireCurrent();
            entry.Leases = checked(entry.Leases + 1);
            return new(this, entry, _state);
        }
    }

    internal IAudioClip CreateClip(AudioFormat format, ReadOnlyMemory<float> samples)
    {
        VerifyOwner();
        PollAvailability();
        IAudioDevice device;
        lock (_gate)
        {
            device = RequireCurrent().Device;
        }

        return device.CreateClip(format, samples.Span);
    }

    internal IAudioClip CreateClipForLease(AudioDeviceLease lease, AudioFormat format, ReadOnlyMemory<float> samples)
    {
        VerifyOwner();
        PollAvailability();
        return lease.GetDevice(this).CreateClip(format, samples.Span);
    }

    internal (AudioDeviceLease Lease, IAudioClip Clip, Exception? ValidationError) CreateClipWithLease(AudioFormat format, ReadOnlyMemory<float> samples)
    {
        AudioDeviceLease lease = AcquireLease();
        IAudioClip clip;
        try
        {
            clip = lease.Device.CreateClip(format, samples.Span);
        }
        catch
        {
            lease.Dispose();
            throw;
        }

        try
        {
            PollAvailability();
            return (lease, clip, null);
        }
        catch (Exception error)
        {
            // Native creation succeeded. Transfer both owners so the caller can retain failed cleanup for retry.
            return (lease, clip, error);
        }
    }

    internal void SetBusGain(AudioBus bus, float gain)
    {
        VerifyOwner();
        PollAvailability();
        IAudioDevice device;
        lock (_gate)
        {
            device = RequireCurrent().Device;
        }

        device.SetBusGain(bus, gain);
    }

    internal void Refresh()
    {
        VerifyOwner();
        if (!PollAvailability())
        {
            return;
        }

        AudioDeviceEntry entry;
        lock (_gate)
        {
            entry = RequireCurrent();
        }

        try
        {
            entry.Device.Update();
        }
        catch (AudioDeviceUnavailableException)
        {
            MarkLost(entry);
        }
        catch
        {
            MarkLost(entry);
            throw;
        }
    }

    internal void Update(in UpdateTick tick) => Refresh();

    internal void BeginClose()
    {
        VerifyOwner(false);
        bool changed;
        lock (_gate)
        {
            changed = !_closing;
            _closing = true;
            foreach (AudioDeviceEntry entry in _entries)
            {
                entry.Lost = true;
                _state = _state with { InvalidatedThroughVersion = Math.Max(_state.InvalidatedThroughVersion, entry.ReadyVersion) };
            }

            if (changed)
            {
                ChangeState(AudioDeviceServiceStatus.Stopping);
            }
        }

        if (changed)
        {
            Notify();
        }
    }

    internal void Close()
    {
        VerifyOwner();
        lock (_gate)
        {
            if (!_closing)
            {
                throw new InvalidOperationException("Audio shutdown has not started.");
            }

            if (_pending is { IsCompleted: false })
            {
                throw new InvalidOperationException("Audio creation is still pending.");
            }

            if (_pending?.IsCompletedSuccessfully == true)
            {
                var entry = new AudioDeviceEntry(_pending.Result, checked(_state.Epoch + 1)) { Lost = true };
                _entries.Add(entry);
                _state = _state with { Epoch = entry.Epoch };
            }

            _pending = null;
        }

        Retire(true);
        lock (_gate)
        {
            if (_entries.Count != 0)
            {
                throw new InvalidOperationException("Return all audio generation leases before stopping the module.");
            }

            _current = null;
            if (_state.Status != AudioDeviceServiceStatus.Stopped)
            {
                ChangeState(AudioDeviceServiceStatus.Stopped);
            }
        }

        Notify();
    }

    internal int Retire(bool all = false)
    {
        VerifyOwner();
        AudioDeviceEntry[] candidates;
        lock (_gate)
        {
            candidates = _entries.Where(entry => entry.Leases == 0 && (all || entry.Lost)).ToArray();
        }

        int count = 0;
        foreach (AudioDeviceEntry entry in candidates)
        {
            entry.Device.Dispose();
            lock (_gate)
            {
                entry.Disposed = true;
                _entries.Remove(entry);
                if (ReferenceEquals(_current, entry))
                {
                    _current = null;
                }
            }

            count++;
        }

        return count;
    }

    internal bool IsUsable(AudioDeviceEntry entry)
    {
        lock (_gate)
        {
            return !entry.Lost && !entry.Disposed && !_closing;
        }
    }

    internal void VerifyUsable(AudioDeviceEntry entry)
    {
        lock (_gate)
        {
            if (entry.Lost || entry.Disposed || _closing)
            {
                throw new AudioDeviceUnavailableException("The audio device generation is unavailable.");
            }
        }
    }

    internal void Release(AudioDeviceEntry entry)
    {
        lock (_gate)
        {
            entry.Leases--;
        }
    }

    private async Task RecoverCoreAsync(TaskCompletionSource<AudioDeviceSnapshot> completion)
    {
        try
        {
            var sender = new AudioDevice(this);
            AudioDeviceSnapshot? current = await sender.BeginRecoveryAsync().ConfigureAwait(false);
            if (current is { } available)
            {
                completion.SetResult(available);
                return;
            }

            Task<IAudioDevice> pending = PendingCreation ?? throw new InvalidOperationException("Audio creation is not pending.");
            try
            {
                await pending.ConfigureAwait(false);
            }
            catch
            {
                // The owner observes the factory error while retaining any cleanup state.
            }

            AudioDeviceSnapshot ready = await sender.CompleteCreateAsync().ConfigureAwait(false);
            completion.SetResult(ready);
        }
        catch (Exception error)
        {
            completion.SetException(error);
        }
    }

    private bool PollAvailability()
    {
        AudioDeviceEntry? entry;
        lock (_gate)
        {
            if (_closing || _state.Status != AudioDeviceServiceStatus.Ready)
            {
                return false;
            }

            entry = _current;
        }

        if (entry is null)
        {
            return false;
        }

        try
        {
            if (entry.Device.OutputState != AudioOutputState.Unavailable)
            {
                return true;
            }
        }
        catch (AudioDeviceUnavailableException)
        {
            MarkLost(entry);
            return false;
        }
        catch
        {
            MarkLost(entry);
            throw;
        }

        MarkLost(entry);
        return false;
    }

    private AudioDeviceEntry RequireCurrent()
    {
        if (_closing || _state.Status != AudioDeviceServiceStatus.Ready || _current is null)
        {
            throw new AudioDeviceUnavailableException("No audio device generation is available.");
        }

        return _current;
    }

    private void MarkLost(AudioDeviceEntry entry)
    {
        bool changed;
        lock (_gate)
        {
            changed = !entry.Lost;
            entry.Lost = true;
            _state = _state with { InvalidatedThroughVersion = Math.Max(_state.InvalidatedThroughVersion, entry.ReadyVersion) };
            if (changed && ReferenceEquals(_current, entry) && !_closing)
            {
                ChangeState(AudioDeviceServiceStatus.Lost);
            }
        }

        if (changed)
        {
            entry.Device.Invalidate();
            Notify();
        }
    }

    private void VerifyOwner(bool established = true)
    {
        int current = Environment.CurrentManagedThreadId;
        if (!established && _ownerThreadId == 0)
        {
            Volatile.Write(ref _ownerThreadId, current);
        }

        if (OwnerThreadId != current)
        {
            throw new InvalidOperationException("Audio device operations must run on the owning thread.");
        }
    }

    private void ChangeState(AudioDeviceServiceStatus status)
        => _state = _state with { Version = checked(_state.Version + 1), Status = status };

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
                Trace.TraceError("Audio state subscriber failed: {0}", error);
            }
        }
    }
}
