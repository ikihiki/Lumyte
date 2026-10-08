using System.Collections.ObjectModel;

namespace Lumyte.Input;

/// <summary>Coordinates injected input sources, device state, history and notifications.</summary>
public sealed class InputSystem : IDisposable
{
    private readonly IInputSource[] _sources;
    private readonly Registry[] _registries;
    private readonly Dictionary<InputDeviceId, Entry> _entries = [];
    private readonly List<Entry> _activeEntries = [];
    private readonly TimeProvider _clock;
    private readonly long _started;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private ulong _nextId;
    private ulong _sequence;
    private bool _busy;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="InputSystem"/> class from injected sources.</summary>
    /// <param name="sources">The borrowed sources, each used by only one system.</param>
    /// <param name="timeProvider">The monotonic clock, defaulting to the system clock.</param>
    public InputSystem(IEnumerable<IInputSource> sources, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = sources.ToArray();
        var seen = new HashSet<IInputSource>(ReferenceEqualityComparer.Instance);
        foreach (IInputSource source in _sources)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (!seen.Add(source))
            {
                throw new ArgumentException("A source instance can only appear once.", nameof(sources));
            }
        }

        _clock = timeProvider ?? TimeProvider.System;
        _started = _clock.GetTimestamp();
        _registries = _sources.Select(source => new Registry(this, source)).ToArray();
        var initialized = new List<Registry>();
        try
        {
            foreach (Registry registry in _registries)
            {
                initialized.Add(registry);
                registry.Allowed = true;
                try
                {
                    registry.Source.Initialize(registry);
                }
                finally
                {
                    registry.Allowed = false;
                }
            }
        }
        catch (Exception exception)
        {
            var errors = new List<Exception> { exception };
            foreach (Registry registry in initialized.AsEnumerable().Reverse())
            {
                Capture(registry.Source.Shutdown, errors);
            }

            foreach (Entry entry in _entries.Values)
            {
                Capture(entry.Device.Dispose, errors);
            }

            _disposed = true;
            throw new AggregateException("Input source initialization failed.", errors);
        }
    }

    /// <summary>Occurs once for each new record, before automatic history pruning.</summary>
    public event Action<InputRecord>? Recorded;

    /// <summary>Gets the injected sources in their fixed update order.</summary>
    public IReadOnlyList<IInputSource> Sources
    {
        get
        {
            Check();
            return Array.AsReadOnly(_sources);
        }
    }

    /// <summary>Gets the active devices, excluding archived disconnected devices.</summary>
    public IReadOnlyDictionary<InputDeviceId, IInputDevice> Devices
    {
        get
        {
            Check();
            return new ReadOnlyDictionary<InputDeviceId, IInputDevice>(
                _entries.Where(pair => pair.Value.State.Connected).ToDictionary(pair => pair.Key, pair => pair.Value.Device));
        }
    }

    /// <summary>Gets device metadata, including disconnected device archives.</summary>
    public IReadOnlyList<InputDeviceInfo> DeviceInfos
    {
        get
        {
            Check();
            return Array.AsReadOnly(_entries.Values.Select(entry => entry.Info).ToArray());
        }
    }

    /// <summary>Updates all sources and devices, publishes input, and prunes configured history.</summary>
    public void Update()
    {
        CheckMutation();
        _busy = true;
        var errors = new List<Exception>();
        var notifications = new List<InputRecord>();
        try
        {
            foreach (Registry registry in _registries)
            {
                registry.Allowed = true;
                try
                {
                    Capture(registry.Source.Update, errors);
                }
                finally
                {
                    registry.Allowed = false;
                }
            }

            foreach (Entry entry in _activeEntries)
            {
                if (entry.State.Connected)
                {
                    Collect(entry, notifications, errors);
                }
            }

            Notify(notifications, errors);
            PruneCore();
            ReleaseRemoved(errors);
        }
        finally
        {
            _busy = false;
        }

        ThrowErrors(errors);
    }

    /// <summary>Gets an immutable array snapshot of retained device records.</summary>
    /// <param name="device">The device identifier.</param>
    /// <returns>The retained records in sequence order.</returns>
    public ReadOnlyMemory<InputRecord> GetRecords(InputDeviceId device)
    {
        Entry entry = GetEntry(device);
        return entry.RecordSnapshot ??= entry.Records.ToArray();
    }

    /// <summary>Gets a snapshot of device state independent of retained history.</summary>
    /// <param name="device">The device identifier.</param>
    /// <returns>The immutable current state.</returns>
    public InputDeviceState GetState(InputDeviceId device)
    {
        Entry entry = GetEntry(device);
        return entry.StateSnapshot ??= new InputDeviceState(entry.State);
    }

    /// <summary>Reads history without consuming it for other readers.</summary>
    /// <param name="device">The device identifier.</param>
    /// <param name="afterSequence">The exclusive cursor, or zero for the beginning.</param>
    /// <returns>The records, next cursor and missing-history indicator.</returns>
    public InputReadResult ReadRecords(InputDeviceId device, ulong afterSequence)
    {
        Entry entry = GetEntry(device);
        CheckSequence(afterSequence);
        return new InputReadResult(
            entry.Records.Where(record => record.Sequence > afterSequence).ToArray(),
            _sequence,
            entry.RemovedThrough > afterSequence);
    }

    /// <summary>Gets the device history retention policy.</summary>
    /// <param name="device">The device identifier.</param>
    /// <returns>The current policy.</returns>
    public InputRetentionPolicy GetRetentionPolicy(InputDeviceId device) => GetEntry(device).Policy;

    /// <summary>Changes a device's policy without immediately deleting history.</summary>
    /// <param name="device">The device identifier.</param>
    /// <param name="policy">The policy to apply during the next pruning operation.</param>
    public void SetRetentionPolicy(InputDeviceId device, InputRetentionPolicy policy)
    {
        CheckMutation();
        ArgumentNullException.ThrowIfNull(policy);
        if (policy.MaxRecords < 0 || policy.MaxAge < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(policy));
        }

        GetEntry(device).Policy = policy;
    }

    /// <summary>Applies all configured retention policies.</summary>
    /// <returns>The total number of removed records.</returns>
    public int Prune()
    {
        CheckMutation();
        return PruneCore();
    }

    /// <summary>Removes records through an inclusive sequence boundary.</summary>
    /// <param name="device">The device identifier.</param>
    /// <param name="sequence">The inclusive sequence boundary.</param>
    /// <returns>The number of removed records.</returns>
    public int RemoveRecordsThrough(InputDeviceId device, ulong sequence)
    {
        CheckMutation();
        Entry entry = GetEntry(device);
        CheckSequence(sequence);
        return RemovePrefix(entry, entry.Records.TakeWhile(record => record.Sequence <= sequence).Count());
    }

    /// <summary>Clears device history without resetting state or identifiers.</summary>
    /// <param name="device">The device identifier.</param>
    /// <returns>The number of removed records.</returns>
    public int ClearRecords(InputDeviceId device)
    {
        CheckMutation();
        Entry entry = GetEntry(device);
        return RemovePrefix(entry, entry.Records.Count);
    }

    /// <summary>Detaches borrowed sources and releases owned devices exactly once.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CheckMutation();
        _busy = true;
        var errors = new List<Exception>();
        var notifications = new List<InputRecord>();
        try
        {
            foreach (Registry registry in _registries.Reverse())
            {
                Capture(registry.Source.Shutdown, errors);
                foreach (Entry entry in _entries.Values.Where(entry => entry.Owner == registry && entry.State.Connected))
                {
                    entry.Removing = true;
                    Collect(entry, notifications, errors);
                }
            }

            Notify(notifications, errors);
            PruneCore();
            ReleaseRemoved(errors);
        }
        finally
        {
            foreach (Entry entry in _entries.Values.Where(entry => !entry.Released))
            {
                entry.Released = true;
                Capture(entry.Device.Dispose, errors);
            }

            Recorded = null;
            _entries.Clear();
            _activeEntries.Clear();
            _disposed = true;
            _busy = false;
        }

        ThrowErrors(errors);
    }

    private static void Capture(Action action, List<Exception> errors)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            errors.Add(exception);
        }
    }

    private static void ThrowErrors(List<Exception> errors)
    {
        if (errors.Count != 0)
        {
            throw new AggregateException("Input processing completed with errors.", errors);
        }
    }

    private static int RemovePrefix(Entry entry, int count)
    {
        if (count > 0)
        {
            entry.RemovedThrough = entry.Records[count - 1].Sequence;
            entry.Records.RemoveRange(0, count);
            entry.RecordSnapshot = null;
        }

        return count;
    }

    private void Check()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _thread)
        {
            throw new InvalidOperationException("InputSystem requires its owning thread.");
        }
    }

    private void CheckMutation()
    {
        Check();
        if (_busy)
        {
            throw new InvalidOperationException("Input mutations cannot reenter an update or notification.");
        }
    }

    private void CheckSequence(ulong sequence)
    {
        if (sequence > _sequence)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }
    }

    private Entry GetEntry(InputDeviceId device)
    {
        Check();
        return _entries.TryGetValue(device, out Entry? entry) ? entry : throw new KeyNotFoundException("Unknown device.");
    }

    private void Collect(Entry entry, List<InputRecord> notifications, List<Exception> errors)
    {
        if (!entry.Published)
        {
            Append(entry, new DeviceConnectedData(entry.Info), notifications);
            entry.Published = true;
        }

        try
        {
            IReadOnlyList<InputData> batch = entry.Device.DrainEvents() ?? throw new ArgumentException("A device returned a null batch.");
            if (batch.Count != 0)
            {
                DeviceState candidate = entry.State.Clone();
                var applied = new List<InputData>(batch.Count);
                foreach (InputData data in batch)
                {
                    candidate.Apply(data, applied);
                }

                entry.State = candidate;
                entry.StateSnapshot = null;
                foreach (InputData data in applied)
                {
                    Append(entry, data, notifications);
                }
            }
        }
        catch (Exception exception)
        {
            errors.Add(exception);
        }

        if (entry.Removing)
        {
            Append(entry, new DeviceDisconnectedData(), notifications);
            var releases = new List<InputData>();
            entry.State.Neutralize(releases);
            entry.StateSnapshot = null;
            entry.State.Connected = false;
            entry.State.Focused = false;
            foreach (InputData data in releases)
            {
                Append(entry, data, notifications);
            }
        }
    }

    private void Append(Entry entry, InputData data, List<InputRecord> notifications)
    {
        var record = new InputRecord(entry.Info.Id, checked(++_sequence), _clock.GetElapsedTime(_started), data);
        entry.Records.Add(record);
        entry.RecordSnapshot = null;
        notifications.Add(record);
    }

    private void Notify(List<InputRecord> notifications, List<Exception> errors)
    {
        foreach (InputRecord record in notifications)
        {
            Delegate[] handlers = Recorded?.GetInvocationList() ?? [];
            foreach (Action<InputRecord> handler in handlers.Cast<Action<InputRecord>>())
            {
                Capture(() => handler(record), errors);
            }
        }
    }

    private int PruneCore()
    {
        TimeSpan now = _clock.GetElapsedTime(_started);
        int removed = 0;
        foreach (Entry entry in _entries.Values)
        {
            int count = entry.Policy.MaxRecords is int maximum ? Math.Max(0, entry.Records.Count - maximum) : 0;
            if (entry.Policy.MaxAge is TimeSpan age)
            {
                count = Math.Max(count, entry.Records.TakeWhile(record => now - record.RecordedAt >= age).Count());
            }

            removed += RemovePrefix(entry, count);
        }

        return removed;
    }

    private void ReleaseRemoved(List<Exception> errors)
    {
        for (int i = _activeEntries.Count - 1; i >= 0; i--)
        {
            Entry entry = _activeEntries[i];
            if (entry.Removing)
            {
                entry.Released = true;
                Capture(entry.Device.Dispose, errors);
                _activeEntries.RemoveAt(i);
            }
        }
    }

    private sealed class Entry(IInputDevice device, Registry owner, InputDeviceInfo info)
    {
        internal IInputDevice Device { get; } = device;

        internal Registry Owner { get; } = owner;

        internal InputDeviceInfo Info { get; } = info;

        internal DeviceState State { get; set; } = new(info.Kind);

        internal List<InputRecord> Records { get; } = [];

        internal InputRetentionPolicy Policy { get; set; } = new();

        internal InputRecord[]? RecordSnapshot { get; set; }

        internal InputDeviceState? StateSnapshot { get; set; }

        internal ulong RemovedThrough { get; set; }

        internal bool Published { get; set; }

        internal bool Removing { get; set; }

        internal bool Released { get; set; }
    }

    private sealed class Registry(InputSystem system, IInputSource source) : IInputDeviceRegistry
    {
        internal IInputSource Source { get; } = source;

        internal bool Allowed { get; set; }

        public InputDeviceId RegisterDevice(IInputDevice device)
        {
            CheckAllowed();
            ArgumentNullException.ThrowIfNull(device);
            if (system._entries.Values.Any(entry => ReferenceEquals(entry.Device, device)))
            {
                throw new ArgumentException("A device instance cannot be registered twice.", nameof(device));
            }

            InputDeviceDescriptor descriptor = device.Descriptor ?? throw new ArgumentException("A descriptor is required.", nameof(device));
            DeviceState.CheckEnum(descriptor.Kind);
            DeviceState.CheckEnum(descriptor.IdentityKind);
            ArgumentNullException.ThrowIfNull(descriptor.Name);
            var id = new InputDeviceId(checked(++system._nextId));
            var info = new InputDeviceInfo(id, descriptor.Kind, descriptor.Name, descriptor.IdentityKind);
            var entry = new Entry(device, this, info);
            system._entries.Add(id, entry);
            system._activeEntries.Add(entry);
            return id;
        }

        public void UnregisterDevice(InputDeviceId device)
        {
            CheckAllowed();
            Entry entry = system.GetEntry(device);
            if (entry.Owner != this || !entry.State.Connected || entry.Removing)
            {
                throw new InvalidOperationException("Only the owning source may unregister an active device once.");
            }

            entry.Removing = true;
        }

        private void CheckAllowed()
        {
            system.Check();
            if (!Allowed)
            {
                throw new InvalidOperationException("Registry operations require a source initialization or update callback.");
            }
        }
    }
}
