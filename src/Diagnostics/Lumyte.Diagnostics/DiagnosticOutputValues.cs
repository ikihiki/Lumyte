using System.Collections;

namespace Lumyte.Diagnostics;

/// <summary>A detached scalar snapshot with direct writing and a lazy dictionary compatibility view.</summary>
public abstract class DiagnosticOutputValues : IReadOnlyDictionary<string, DiagnosticValue>
{
    private Dictionary<string, DiagnosticValue>? _materialized;

    /// <summary>Gets the number of emitted fields.</summary>
    public abstract int Count { get; }

    /// <inheritdoc/>
    public IEnumerable<string> Keys => Materialize().Keys;

    /// <inheritdoc/>
    public IEnumerable<DiagnosticValue> Values => Materialize().Values;

    /// <inheritdoc/>
    public DiagnosticValue this[string key] => Materialize()[key];

    /// <summary>Writes the detached fields; implementations must emit exactly Count unique names.</summary>
    /// <typeparam name="TWriter">The selected scalar writer.</typeparam>
    /// <param name="writer">The synchronous writer, which must not be retained.</param>
    public abstract void WriteTo<TWriter>(ref TWriter writer)
        where TWriter : IDiagnosticValueWriter, allows ref struct;

    /// <inheritdoc/>
    public bool ContainsKey(string key) => Materialize().ContainsKey(key);

    /// <inheritdoc/>
    public bool TryGetValue(string key, out DiagnosticValue value) => Materialize().TryGetValue(key, out value);

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, DiagnosticValue>> GetEnumerator() => Materialize().GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private Dictionary<string, DiagnosticValue> Materialize()
    {
        if (_materialized != null)
        {
            return _materialized;
        }

        var collector = new Collector(new(Count, StringComparer.Ordinal));
        WriteTo(ref collector);
        return Interlocked.CompareExchange(ref _materialized, collector.Items, null) ?? collector.Items;
    }

    private readonly struct Collector(Dictionary<string, DiagnosticValue> items) : IDiagnosticValueWriter
    {
        public Dictionary<string, DiagnosticValue> Items => items;

        public void Write(string name, bool value) => items.Add(name, DiagnosticValue.From(value));

        public void Write(string name, long value) => items.Add(name, DiagnosticValue.From(value));

        public void Write(string name, double value) => items.Add(name, DiagnosticValue.From(value));

        public void Write(string name, string value) => items.Add(name, DiagnosticValue.From(value));
    }
}
