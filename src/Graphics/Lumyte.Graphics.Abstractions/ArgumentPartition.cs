namespace Lumyte.Graphics.Abstractions;

internal sealed class ArgumentPartition(ArgumentTableBindingLimits limits) : IDisposable
{
    private readonly Dictionary<ArgumentKey, Entry> _entries = [];
    private readonly Dictionary<ArgumentKey, Registration> _registrations = [];
    private readonly int[] _counts = new int[3];

    internal IArgumentTable? Table { get; private set; }

    internal int TextureCount => _counts[0];

    internal int SamplerCount => _counts[1];

    internal int BufferCount => _counts[2];

    internal IEnumerable<ResolvedArgument> PlanningValues => _entries.Values.Select(entry => entry.Value);

    public void Dispose()
    {
        Table?.Dispose();
        Table = null;
        _registrations.Clear();
    }

    internal bool Fits(IEnumerable<ResolvedArgument> values, uint colorAttachments)
    {
        var additions = new Dictionary<ArgumentKey, int>();
        int[] next = (int[])_counts.Clone();
        uint[] spans = [limits.MinimumTextureBindings, limits.MinimumSamplerBindings, 0];
        var resources = new HashSet<ArgumentKey>();
        var read = new HashSet<ArgumentKey>();
        var write = new HashSet<ArgumentKey>();
        uint textures = 0;
        uint samplers = 0;

        // The selected table is bound as a whole, including registrations not
        // referenced by this operation. Budget its complete physical shape.
        foreach (ResolvedArgument value in PlanningValues.Concat(values))
        {
            ArgumentKey key = value.Key;
            int kind = key.Namespace;
            int slot;
            if (_entries.TryGetValue(key, out Entry? existing))
            {
                slot = existing.Slot;
            }
            else if (!additions.TryGetValue(key, out slot))
            {
                slot = next[kind]++;
                additions.Add(key, slot);
            }

            spans[kind] = Math.Max(spans[kind], checked((uint)slot + 1));
            if (!resources.Add(key))
            {
                continue;
            }

            if (kind == 0)
            {
                textures++;
            }
            else if (kind == 1)
            {
                samplers++;
            }
            else
            {
                ArgumentKey physical = key with { Type = typeof(void), Kind = 2, Count = key.Size, Stride = 1, Access = ArgumentBufferAccess.Read };

                // ReadWrite is one writable descriptor that also supports logical reads.
                if (key.Access == ArgumentBufferAccess.Read)
                {
                    read.Add(physical);
                }

                if ((key.Access & ArgumentBufferAccess.Write) != 0)
                {
                    write.Add(physical);
                }
            }
        }

        if ((uint)next[0] > limits.TextureSlots || (uint)next[1] > limits.SamplerSlots || (uint)next[2] > limits.BufferSlots)
        {
            return false;
        }

        uint buffers = checked((uint)(read.Count + write.Count));
        uint t = limits.DirectSlotBindings ? spans[0] : Math.Max(textures, limits.MinimumTextureBindings);
        uint s = limits.DirectSlotBindings ? spans[1] : Math.Max(samplers, limits.MinimumSamplerBindings);
        uint b = limits.DirectSlotBindings ? spans[2] : buffers;
        ulong combined = ((ulong)t * limits.TextureBindingWeight) + ((ulong)s * limits.SamplerBindingWeight)
            + ((ulong)b * limits.BufferBindingWeight) + limits.ReservedBindings + ((ulong)colorAttachments * limits.ColorAttachmentBindingWeight);
        return t <= limits.TexturesPerDraw && s <= limits.SamplersPerDraw && b <= limits.BuffersPerDraw && combined <= limits.CombinedBindings;
    }

    internal void Append(IEnumerable<ResolvedArgument> values)
    {
        foreach (ResolvedArgument value in values)
        {
            if (!_entries.ContainsKey(value.Key))
            {
                int kind = value.Key.Namespace;
                _entries.Add(value.Key, new(value, _counts[kind]++));
            }
        }
    }

    internal void Create(IGraphicDevice device, CancellationToken cancellationToken)
    {
        Table = device.CreateArgumentTable(new() { TextureCapacity = checked((uint)TextureCount), SamplerCapacity = checked((uint)SamplerCount), BufferCapacity = checked((uint)BufferCount) });
        foreach ((ArgumentKey key, Entry entry) in _entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            entry.Reference = entry.Value.Write(Table, checked((uint)entry.Slot));
            _registrations.Add(key, new(checked((uint)entry.Slot)));
        }
    }

    internal object Reference(ArgumentKey key) => _entries[key].Reference!;

    internal void ClearPlanning() => _entries.Clear();

    internal void RetainRegistrations(IEnumerable<ArgumentKey> keys)
    {
        foreach (ArgumentKey key in keys)
        {
            _registrations[key].Users++;
        }
    }

    internal void ReleaseRegistration(ArgumentKey key)
    {
        if (Table is null || !_registrations.TryGetValue(key, out Registration? registration))
        {
            return;
        }

        if (registration.Users > 1)
        {
            registration.Users--;
            return;
        }

        // Publish the decrement only after native release succeeds, so cleanup can retry.
        switch (key.Namespace)
        {
            case 0: Table.ReleaseTexture(registration.Slot); break;
            case 1: Table.ReleaseSampler(registration.Slot); break;
            default: Table.ReleaseBuffer(registration.Slot); break;
        }

        _registrations.Remove(key);
    }

    private sealed class Registration(uint slot)
    {
        internal uint Slot { get; } = slot;

        internal int Users { get; set; }
    }

    private sealed class Entry(ResolvedArgument value, int slot)
    {
        internal ResolvedArgument Value { get; } = value;

        internal int Slot { get; } = slot;

        internal object? Reference { get; set; }
    }
}
