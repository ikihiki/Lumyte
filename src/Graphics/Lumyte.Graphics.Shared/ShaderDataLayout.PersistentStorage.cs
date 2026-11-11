using System.Collections;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

/// <summary>Owns mutable wire bytes separately from immutable per-copy CPU snapshots.</summary>
public sealed partial class ShaderDataLayout
{
    private sealed class PersistentStorage<T> : ShaderDataStorage<T>
        where T : struct, IShaderData
    {
        private readonly Field[] _fields;
        private readonly Dictionary<string, int> _indices;
        private readonly ShaderValue?[][] _references;
        private readonly Func<ShaderValue, string, byte[]> _encoder;
        private readonly string _root;

        internal PersistentStorage(ShaderDataLayout layout, object owner, ShaderDataMember[] schema, ReadOnlySpan<T> initial, Func<ShaderValue, string, byte[]> encoder)
        {
            Layout = layout;
            Owner = owner;
            _encoder = encoder;
            _root = T.RootParameter;
            ArgumentException.ThrowIfNullOrWhiteSpace(_root);
            Count = initial.Length;
            Bytes = new byte[checked(Count * layout.Size)];
            _fields = schema.Select(s => new Field(s, layout._members[s.Path])).ToArray();
            _indices = schema.Select((s, i) => (s.Path, Index: i)).ToDictionary(p => p.Path, p => p.Index, StringComparer.Ordinal);
            _references = new ShaderValue?[Count][];
            for (int i = 0; i < Count; i++)
            {
                Set(i, in initial[i]);
            }
        }

        public override int Count { get; }

        internal object Owner { get; }

        internal ShaderDataLayout Layout { get; }

        internal byte[] Bytes { get; }

        public override void Set(int index, in T value)
        {
            ValidateIndex(index);
            ShaderValueSnapshot snapshot = IShaderArguments.Capture(in value);
            if (snapshot.RootParameter != _root || snapshot.ShaderTypeName != Layout.Name || snapshot.Values.Count != _fields.Length)
            {
                throw new ArgumentException("The codec differs from the generated persistent schema.", nameof(value));
            }

            byte[] bytes = new byte[Layout.Size];
            var references = new ShaderValue?[_fields.Length];
            bool[] visited = new bool[_fields.Length];
            foreach (ShaderValue leaf in snapshot.Values)
            {
                int fieldIndex = Find(leaf.Path, leaf.ValueType, leaf.IsReference);
                if (visited[fieldIndex])
                {
                    throw new ArgumentException("The codec emitted a duplicate member.", nameof(value));
                }

                visited[fieldIndex] = true;
                Field field = _fields[fieldIndex];
                Span<byte> destination = bytes.AsSpan(field.Member.Offset, field.Member.Size);
                if (leaf.IsReference)
                {
                    var wire = NativeReference.Encode(_encoder(leaf, field.Member.Kind));
                    wire.Write(destination);
                    references[fieldIndex] = leaf;
                }
                else
                {
                    if (leaf.Data.Length != field.Member.Size)
                    {
                        throw new ArgumentException("The codec emitted an incompatible numeric size.", nameof(value));
                    }

                    leaf.Data.Span.CopyTo(destination);
                }
            }

            bytes.CopyTo(Bytes, checked(index * Layout.Size));
            _references[index] = references;
        }

        public override void SetValue<TValue>(int index, ShaderDataValueMember<T, TValue> member, in TValue value)
        {
            ValidateIndex(index);
            int fieldIndex = Find(member.Path, typeof(TValue), false);
            ShaderMember field = _fields[fieldIndex].Member;
            MemoryMarshal.Write(Bytes.AsSpan(checked((index * Layout.Size) + field.Offset), field.Size), in value);
        }

        public override void SetReference<TReference>(int index, ShaderDataReferenceMember<T, TReference> member, IGpuRef<TReference> value)
        {
            ValidateIndex(index);
            ArgumentNullException.ThrowIfNull(value);
            int fieldIndex = Find(member.Path, typeof(TReference), true);
            ShaderMember field = _fields[fieldIndex].Member;
            var logical = new ShaderValue(member.Path, typeof(TReference), default, value, true);
            var wire = NativeReference.Encode(_encoder(logical, field.Kind));
            wire.Write(Bytes.AsSpan(checked((index * Layout.Size) + field.Offset), field.Size));
            _references[index][fieldIndex] = logical;
        }

        internal ShaderValueSnapshot Freeze(int index)
        {
            byte[] bytes = Bytes.AsSpan(checked(index * Layout.Size), Layout.Size).ToArray();
            var references = (ShaderValue?[])_references[index].Clone();
            return new(_root, Layout.Name, new FrozenValues(_fields, bytes, references));
        }

        private int Find(string path, Type type, bool reference)
        {
            if (path == null || !_indices.TryGetValue(path, out int index) || _fields[index].Schema.ValueType != type || _fields[index].Schema.IsReference != reference)
            {
                throw new ArgumentException("The member token or codec does not match the generated schema.", nameof(path));
            }

            return index;
        }

        private void ValidateIndex(int index) => ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)Count, nameof(index));
    }

    private sealed record Field(ShaderDataMember Schema, ShaderMember Member);

    // Does not retain PersistentStorage, its mutable backing, its owner device, or a whole-copy array.
    private sealed class FrozenValues(Field[] fields, byte[] bytes, ShaderValue?[] references) : IReadOnlyList<ShaderValue>
    {
        public int Count => fields.Length;

        public ShaderValue this[int index]
        {
            get
            {
                Field field = fields[index];
                ShaderDataMember schema = field.Schema;
                return schema.IsReference
                    ? references[index]!
                    : new(schema.Path, schema.ValueType, bytes.AsMemory(field.Member.Offset, field.Member.Size), null, false);
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
    }
}
