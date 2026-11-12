using System.Collections;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

/// <summary>Captures and packs contiguous logical shader data.</summary>
public sealed partial class ShaderDataLayout
{
    /// <summary>Captures every codec once before packing a contiguous upload batch.</summary>
    /// <typeparam name="T">The logical shader data type.</typeparam>
    /// <param name="source">The logical source values.</param>
    /// <param name="reference">The backend opaque reference encoder.</param>
    /// <param name="snapshots">Immutable element metadata published only after upload succeeds.</param>
    /// <returns>The complete packed batch.</returns>
    public byte[] Pack<T>(ReadOnlySpan<T> source, Func<ShaderValue, string, byte[]> reference, out ShaderValueSnapshot[] snapshots)
        where T : struct, IShaderData
    {
        snapshots = new ShaderValueSnapshot[source.Length];
        byte[] bytes = new byte[checked(source.Length * Size)];
        var writer = new BulkWriter(this, bytes);
        for (int i = 0; i < source.Length; i++)
        {
            string root = T.RootParameter;
            string name = T.ShaderTypeName;
            ArgumentException.ThrowIfNullOrWhiteSpace(root);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            writer.Begin(i * Size);
            source[i].Write(ref writer);
            snapshots[i] = new(root, name, writer.Finish());
        }

        // Preserve capture-all-before-pack, callback order and the generic error path.
        for (int i = 0; i < snapshots.Length; i++)
        {
            if (snapshots[i].Values is NumericValues && snapshots[i].ShaderTypeName == Name)
            {
                continue;
            }

            Pack(snapshots[i], reference).CopyTo(bytes, i * Size);
        }

        // The public packed result is caller-owned; immutable metadata owns a separate
        // numeric-only clone. Reference wire bytes must never be exposed through Read.
        byte[]? numeric = null;
        for (int i = 0; i < snapshots.Length; i++)
        {
            if (snapshots[i].Values is NumericValues values)
            {
                numeric ??= new byte[bytes.Length];
                bytes.AsSpan(i * Size, Size).CopyTo(numeric.AsSpan(i * Size, Size));
                values.SetStorage(numeric);
            }
        }

        return bytes;
    }

    internal static bool IsNumericBatch(ShaderValueSnapshot snapshot) => snapshot.Values is NumericValues;

    private sealed class BulkWriter(ShaderDataLayout layout, byte[] bytes) : IShaderValueWriter
    {
        private readonly KeyValuePair<string, ShaderMember>[] _plan = layout._bulkPlan;
        private readonly bool _safe = layout._bulkSafe;
        private int _start;
        private int _index;
        private List<ShaderValue>? _fallback;

        public void WriteValue<T>(string path, in T value)
            where T : unmanaged
        {
            Type type = typeof(T);
            if (type != typeof(int) && type != typeof(uint) && type != typeof(float)
                && type != typeof(System.Numerics.Vector2) && type != typeof(System.Numerics.Vector3)
                && type != typeof(System.Numerics.Vector4) && type != typeof(System.Numerics.Matrix4x4))
            {
                throw new NotSupportedException("Unsupported numeric shader member type.");
            }

            T copy = value;
            ReadOnlySpan<byte> payload = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref copy, 1));
            if (_fallback is null && _index < _plan.Length)
            {
                KeyValuePair<string, ShaderMember> member = _plan[_index];
                if (member.Key == path && !member.Value.Reference && member.Value.ValueType == type && member.Value.Size == payload.Length)
                {
                    payload.CopyTo(bytes.AsSpan(_start + member.Value.Offset, payload.Length));
                    _index++;
                    return;
                }
            }

            Fallback();
            _fallback!.Add(new(path, type, payload.ToArray(), null, false));
        }

        public void WriteReference<T>(string path, IGpuRef<T>? value)
        {
            Fallback();
            _fallback!.Add(new(path, typeof(T), default, value, true));
        }

        internal static bool IsSafe(ShaderDataLayout layout)
        {
            ShaderMember[] members = layout._members.Values.OrderBy(member => member.Offset).ToArray();
            int end = 0;
            foreach (ShaderMember member in members)
            {
                if (member.Offset < end || member.Size < 0 || member.Offset > layout.Size || member.Size > layout.Size - member.Offset)
                {
                    return false;
                }

                end = member.Offset + member.Size;
            }

            return true;
        }

        internal void Begin(int start)
        {
            _start = start;
            _index = 0;
            _fallback = _safe ? null : [];
        }

        internal IReadOnlyList<ShaderValue> Finish()
        {
            if (_fallback is null && _index == _plan.Length)
            {
                return new NumericValues(_plan, bytes, _start);
            }

            Fallback();
            return _fallback!.AsReadOnly();
        }

        private void Fallback()
        {
            if (_fallback is not null)
            {
                return;
            }

            _fallback = new();
            for (int i = 0; i < _index; i++)
            {
                KeyValuePair<string, ShaderMember> member = _plan[i];

                // Detach the prefix: later duplicate fields can overwrite their wire offset.
                _fallback.Add(new(member.Key, member.Value.ValueType!, bytes.AsMemory(_start + member.Value.Offset, member.Value.Size).ToArray(), null, false));
            }
        }
    }

    private sealed class NumericValues(KeyValuePair<string, ShaderMember>[] plan, byte[] bytes, int start) : IReadOnlyList<ShaderValue>
    {
        private byte[] _storage = bytes;

        public int Count => plan.Length;

        public ShaderValue this[int index]
        {
            get
            {
                KeyValuePair<string, ShaderMember> member = plan[index];
                return new(member.Key, member.Value.ValueType!, _storage.AsMemory(start + member.Value.Offset, member.Value.Size), null, false);
            }
        }

        public IEnumerator<ShaderValue> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
            {
                yield return this[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        internal void SetStorage(byte[] storage) => _storage = storage;
    }
}
