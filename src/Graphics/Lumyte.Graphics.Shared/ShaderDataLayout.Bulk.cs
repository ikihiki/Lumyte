using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

/// <summary>Captures and packs contiguous logical shader data.</summary>
public sealed partial class ShaderDataLayout
{
    private readonly ConcurrentDictionary<Type, KeyValuePair<string, ShaderMember>[]> _numericWirePlans = new();

    /// <summary>Copies explicit numeric C# wire spans or captures fixed-offset codecs once before batch serialization.</summary>
    /// <typeparam name="T">The logical shader data type.</typeparam>
    /// <param name="source">The logical source values.</param>
    /// <param name="reference">The backend opaque reference encoder.</param>
    /// <param name="snapshots">Immutable element metadata published only after upload succeeds.</param>
    /// <returns>The complete packed batch.</returns>
    public byte[] Pack<T>(ReadOnlySpan<T> source, Func<ShaderValue, string, byte[]> reference, out ShaderValueSnapshot[] snapshots)
        where T : struct, IShaderData
    {
        if (typeof(IShaderNumericWireData).IsAssignableFrom(typeof(T)))
        {
            return PackNumericWire(source, out snapshots);
        }

        snapshots = new ShaderValueSnapshot[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            snapshots[i] = IShaderArguments.Capture(in source[i]);
        }

        byte[] bytes = new byte[checked(source.Length * Size)];
        for (int i = 0; i < snapshots.Length; i++)
        {
            Pack(snapshots[i], reference).CopyTo(bytes, i * Size);
        }

        return bytes;
    }

    /// <summary>Validates an explicit numeric C# wire opt-in before backend allocation.</summary>
    /// <typeparam name="T">The logical shader-data type, optionally implementing IShaderNumericWireData.</typeparam>
    public void ValidateNumericWireData<T>()
        where T : struct, IShaderData
    {
        if (typeof(IShaderNumericWireData).IsAssignableFrom(typeof(T)))
        {
            _ = _numericWirePlans.GetOrAdd(typeof(T), _ => ValidateNumericWire<T>());
        }
    }

    internal static bool IsNumericBatch(ShaderValueSnapshot snapshot) => snapshot.Values is NumericValues;

    private static int NumericStorageSize(Type type) => type == typeof(int) || type == typeof(uint) || type == typeof(float) ? 4
        : type == typeof(System.Numerics.Vector2) ? 8
        : type == typeof(System.Numerics.Vector3) ? 12
        : type == typeof(System.Numerics.Vector4) ? 16
        : type == typeof(System.Numerics.Matrix4x4) ? 64 : 0;

    private byte[] PackNumericWire<T>(ReadOnlySpan<T> source, out ShaderValueSnapshot[] snapshots)
        where T : struct, IShaderData
    {
        KeyValuePair<string, ShaderMember>[] plan = _numericWirePlans.GetOrAdd(typeof(T), _ => ValidateNumericWire<T>());
        string root = T.RootParameter;
        string name = T.ShaderTypeName;
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        int length = checked(source.Length * Size);
        ref byte first = ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(source));
        byte[] bytes = MemoryMarshal.CreateReadOnlySpan(ref first, length).ToArray();
        byte[] numeric = (byte[])bytes.Clone();
        snapshots = new ShaderValueSnapshot[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            snapshots[i] = new(root, name, new NumericValues(plan, numeric, i * Size)) { SizeInBytes = Size };
        }

        return bytes;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2090", Justification = "Generated numeric wire metadata roots all instance fields. Missing field metadata fails explicit coverage validation; it never enables an unchecked copy.")]
    private KeyValuePair<string, ShaderMember>[] ValidateNumericWire<T>()
        where T : struct, IShaderData
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>() || !typeof(T).IsExplicitLayout || !BitConverter.IsLittleEndian)
        {
            throw new NotSupportedException("Direct numeric wire storage cannot contain managed or GPU interface references.");
        }

        ShaderNumericWireLayout wire = ((IShaderNumericWireData)(object)default(T)).NumericWireLayout;
        if (wire.Stride != Unsafe.SizeOf<T>() || wire.Stride != Size || !_bulkSafe || wire.Members.Count != _members.Count || T.ShaderTypeName != Name)
        {
            throw new NotSupportedException("The shader does not match the authoritative C# numeric wire stride and members.");
        }

        FieldInfo[] storageFields = typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (storageFields.Length != wire.Members.Count)
        {
            throw new NotSupportedException("Numeric wire metadata does not cover every C# storage field.");
        }

        foreach (FieldInfo field in storageFields)
        {
            string name = field.Name.StartsWith("<", StringComparison.Ordinal) && field.Name.EndsWith(">k__BackingField", StringComparison.Ordinal)
                ? field.Name.Substring(1, field.Name.IndexOf('>') - 1) : field.Name;
            ShaderNumericWireMember? declared = wire.Members.SingleOrDefault(member => member.Path == name);
            if (declared is null || declared.ValueType != field.FieldType || declared.Size != NumericStorageSize(field.FieldType) || declared.Offset != Marshal.OffsetOf<T>(field.Name).ToInt32())
            {
                throw new NotSupportedException("Numeric wire metadata differs from an actual C# storage field.");
            }
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(T.RootParameter);
        var plan = new KeyValuePair<string, ShaderMember>[wire.Members.Count];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < plan.Length; i++)
        {
            ShaderNumericWireMember field = wire.Members[i];
            if (!seen.Add(field.Path) || !_members.TryGetValue(field.Path, out ShaderMember? member) || member.Reference
                || member.ValueType != field.ValueType || member.Offset != field.Offset || member.Size != field.Size)
            {
                throw new NotSupportedException("The shader does not match an authoritative C# numeric wire field.");
            }

            plan[i] = new(field.Path, new(field.Offset, field.Size, false, member.Kind, field.ValueType, null, i));
        }

        return plan;
    }

    private sealed class NumericValues(KeyValuePair<string, ShaderMember>[] plan, byte[] bytes, int start) : IReadOnlyList<ShaderValue>
    {
        private readonly byte[] _storage = bytes;

        public int Count => plan.Length;

        public ShaderValue this[int index]
        {
            get
            {
                KeyValuePair<string, ShaderMember> member = plan[index];
                return new(member.Value.Offset, member.Key, member.Value.ValueType!, _storage.AsMemory(start + member.Value.Offset, member.Value.Size), null, false);
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
