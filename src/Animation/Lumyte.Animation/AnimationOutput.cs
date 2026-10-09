namespace Lumyte.Animation;

/// <summary>Stores typed animation results in reusable slots; consumers own value application.</summary>
public sealed class AnimationOutput
{
    private readonly Dictionary<object, object> _slots = [];
    private long _epoch;

    /// <summary>Clears a typed storage slot without boxing its value.</summary>
    private interface ISlot
    {
        /// <summary>Clears contributions while retaining reusable typed storage.</summary>
        void Clear();

        /// <summary>Copies an active typed contribution to another output.</summary>
        /// <param name="channel">The channel identity.</param>
        /// <param name="output">The destination output.</param>
        void CopyTo(object channel, AnimationOutput output);
    }

    /// <summary>Clears contributions while retaining reusable typed storage.</summary>
    public void Clear()
    {
        foreach (ISlot slot in _slots.Values)
        {
            slot.Clear();
        }
    }

    /// <summary>Retrieves a computed channel value; returns false when no contribution exists.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="channel">The channel.</param>
    /// <param name="value">The value.</param>
    /// <returns>The operation result.</returns>
    public bool TryGet<T>(AnimationChannel<T> channel, out T value)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (_slots.TryGetValue(channel, out object? entry) && entry is Slot<T> { HasValue: true } slot)
        {
            value = slot.Value;
            return true;
        }

        value = default!;
        return false;
    }

    internal void CopyTo(AnimationOutput output)
    {
        output.BeginEvaluation();
        foreach (KeyValuePair<object, object> entry in _slots)
        {
            ((ISlot)entry.Value).CopyTo(entry.Key, output);
        }
    }

    internal void BeginEvaluation() => _epoch = checked(_epoch + 1);

    internal void Set<T>(AnimationChannel<T> channel, T value, UInt128 priority)
    {
        if (!_slots.TryGetValue(channel, out object? entry))
        {
            entry = new Slot<T>();
            _slots.Add(channel, entry);
        }

        var slot = (Slot<T>)entry;
        if (slot.HasValue && slot.Epoch == _epoch && slot.Priority > priority)
        {
            return;
        }

        slot.Epoch = _epoch;
        slot.Priority = priority;
        slot.Value = value;
        slot.HasValue = true;
    }

    private sealed class Slot<T> : ISlot
    {
        /// <summary>Gets or sets the value.</summary>
        public T Value { get; set; } = default!;

        /// <summary>Gets or sets a value indicating whether has value.</summary>
        public bool HasValue { get; set; }

        internal long Epoch { get; set; }

        internal UInt128 Priority { get; set; }

        /// <summary>Copies an active typed contribution without boxing its value.</summary>
        /// <param name="channel">The channel identity.</param>
        /// <param name="output">The destination output.</param>
        public void CopyTo(object channel, AnimationOutput output)
        {
            if (HasValue)
            {
                output.Set((AnimationChannel<T>)channel, Value, 0);
            }
        }

        /// <summary>Clears contributions while retaining reusable typed storage.</summary>
        public void Clear()
        {
            Value = default!;
            HasValue = false;
        }
    }
}
