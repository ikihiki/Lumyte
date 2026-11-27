using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lumyte.Graphics.Abstractions;

/// <summary>Copies ordinary unmanaged values without interfaces, generated code or conversion.</summary>
public static class ShaderTransfer
{
    /// <summary>Validates the static wire identity of an alternate codec before writing.</summary>
    /// <typeparam name="TTarget">The allocation's declared shader type.</typeparam>
    /// <typeparam name="TSource">The alternate encoding type.</typeparam>
    /// <remarks>Metadata providers must be immutable and instance independent. Equal metadata does not prove field layout equality, which remains the codec author's precondition. TransferFields is optional diagnostics and is never consulted.</remarks>
    /// <exception cref="ArgumentException">The codecs declare different wire identities.</exception>
    public static void ValidateCompatibility<TTarget, TSource>()
        where TTarget : struct, IShaderArguments
        where TSource : struct, IShaderArguments
    {
        if (!string.Equals(TTarget.RootParameter, TSource.RootParameter, StringComparison.Ordinal)
            || !string.Equals(TTarget.ShaderTypeName, TSource.ShaderTypeName, StringComparison.Ordinal)
            || TTarget.TransferSize != TSource.TransferSize
            || TTarget.ReferenceCount != TSource.ReferenceCount)
        {
            throw new ArgumentException("Alternate shader codec must declare the target wire identity.");
        }
    }

    /// <summary>Copies the complete C# representation, including padding, into an independent snapshot.</summary>
    /// <typeparam name="T">The unmanaged value type.</typeparam>
    /// <param name="value">The value to copy.</param>
    /// <param name="rootParameter">The shader root parameter name.</param>
    /// <param name="shaderTypeName">The shader structure name, or the C# type name by default.</param>
    /// <returns>The independent numeric snapshot, without resource reference fixups.</returns>
    public static ShaderValueSnapshot Capture<T>(in T value, string rootParameter = "arguments", string? shaderTypeName = null)
        where T : unmanaged
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootParameter);
        string name = shaderTypeName ?? TypeName<T>();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new(rootParameter, name, MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)).ToArray(), [], []) { DiagnosticType = typeof(T) };
    }

    /// <summary>Gets the conventional shader name for an unmanaged C# type.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <returns>The intrinsic shader name or C# type name.</returns>
    public static string TypeName<T>()
        where T : unmanaged => TypeName(typeof(T));

    /// <summary>Gets previously initialized diagnostics, or an empty list when metadata is unavailable.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <returns>Diagnostic metadata only; it never determines transferred bytes.</returns>
    public static IReadOnlyList<ShaderTransferField> Fields<T>()
        where T : unmanaged => Fields(typeof(T));

    /// <summary>Explicitly gets cached diagnostic metadata for a shader argument struct.</summary>
    /// <typeparam name="T">The application shader argument type.</typeparam>
    /// <param name="value">The value identifying the diagnostic type; its contents are not inspected.</param>
    /// <returns>Static diagnostic metadata, without retaining or boxing the value.</returns>
    public static IReadOnlyList<ShaderTransferField> Fields<T>(in T value)
        where T : struct, IShaderArguments => ArgumentFields<T>();

    /// <summary>Gets previously initialized CPU diagnostic metadata; an empty list means unavailable.</summary>
    /// <remarks>Use the constrained argument overload for static wire metadata. Ordinary unmanaged reflection is an explicit opt-in through <see cref="ReflectFields"/> and requires preserved fields. Type-only lookup never constructs a generic method at runtime.</remarks>
    /// <param name="type">The C# value type to inspect.</param>
    /// <returns>Diagnostic metadata only; it never determines transferred bytes.</returns>
    public static IReadOnlyList<ShaderTransferField> Fields(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!type.IsValueType)
        {
            throw new ArgumentException("Type-based shader diagnostics require a value type.", nameof(type));
        }

        return DiagnosticCache.Fields.TryGetValue(type, out DiagnosticMetadata? metadata) ? metadata.Fields : [];
    }

    /// <summary>Explicitly reflects and caches ordinary blittable C# field diagnostics without emitted code.</summary>
    /// <remarks>This optional inspection requires all recursively inspected fields to be preserved. Managed wrappers, automatic layouts and non-blittable primitive fields are rejected rather than reporting marshalled offsets as managed wire offsets. Argument wire metadata must instead be obtained through the constrained argument overload.</remarks>
    /// <param name="type">The ordinary blittable value type whose field metadata is preserved.</param>
    /// <returns>Diagnostic field metadata, unrelated to transfer serialization.</returns>
    [RequiresUnreferencedCode("Preserve all instance fields of the inspected type and its nested field types, or use static IShaderArguments.TransferFields metadata.")]
    public static IReadOnlyList<ShaderTransferField> ReflectFields(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!type.IsValueType || typeof(IShaderArguments).IsAssignableFrom(type) || !IsBlittable(type))
        {
            throw new ArgumentException("Reflection diagnostics require an ordinary blittable value type with sequential or explicit layout.", nameof(type));
        }

        if (DiagnosticCache.Fields.TryGetValue(type, out DiagnosticMetadata? metadata))
        {
            return metadata.Fields;
        }

        List<ShaderTransferField> fields = [];
        Describe(type, string.Empty, 0, fields);
        return DiagnosticCache.Fields.GetValue(type, _ => new(fields.AsReadOnly())).Fields;
    }

    // Capturing this method group does not initialize the diagnostic holder.
    internal static IReadOnlyList<ShaderTransferField> ArgumentFields<T>()
        where T : struct, IShaderArguments => ArgumentDiagnosticCache<T>.Fields;

    private static string TypeName(Type type) => type == typeof(int) ? "int"
        : type == typeof(uint) ? "uint"
        : type == typeof(float) ? "float"
        : type == typeof(Vector2) ? "float2"
        : type == typeof(Vector3) ? "float3"
        : type == typeof(Vector4) ? "float4"
        : type == typeof(Matrix4x4) ? "float4x4" : type.Name;

    private static int LeafSize(Type type)
    {
        if (type.IsEnum)
        {
            return LeafSize(Enum.GetUnderlyingType(type));
        }

        return type == typeof(byte) || type == typeof(sbyte) ? 1
            : type == typeof(short) || type == typeof(ushort) || type == typeof(Half) ? 2
            : type == typeof(int) || type == typeof(uint) || type == typeof(float) ? 4
            : type == typeof(long) || type == typeof(ulong) || type == typeof(double) || type == typeof(Vector2) ? 8
            : type == typeof(Vector3) ? 12
            : type == typeof(Vector4) ? 16
            : type == typeof(Matrix4x4) ? 64
            : type == typeof(nint) || type == typeof(nuint) || type.IsPointer ? IntPtr.Size : 0;
    }

    [RequiresUnreferencedCode("Recursively inspects instance fields of the supplied type.")]
    private static bool IsBlittable(Type type) => LeafSize(type) != 0
        || (type.IsValueType && !type.IsPrimitive && !type.IsAutoLayout
            && type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).All(field => IsBlittable(field.FieldType)));

    [RequiresUnreferencedCode("Recursively inspects instance fields of the supplied type.")]
    private static void Describe(Type type, string prefix, int offset, List<ShaderTransferField> fields)
    {
        foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            string name = field.Name.StartsWith('<') ? field.Name[1..field.Name.IndexOf('>')] : field.Name;
            string path = prefix + name;
            int location = checked(offset + Marshal.OffsetOf(type, field.Name).ToInt32());
            Type member = field.FieldType;
            int size = LeafSize(member);
            if (size != 0)
            {
                fields.Add(new(path, location, size, member));
            }
            else
            {
                Describe(member, path + ".", location, fields);
            }
        }
    }

    private static class ArgumentDiagnosticCache<T>
        where T : struct, IShaderArguments
    {
        internal static readonly IReadOnlyList<ShaderTransferField> Fields = Create();

        static ArgumentDiagnosticCache()
        {
        }

        private static IReadOnlyList<ShaderTransferField> Create()
        {
            // Static constrained dispatch is available on NativeAOT and browser WASM.
            IReadOnlyList<ShaderTransferField> fields = T.TransferFields;
            return DiagnosticCache.Fields.GetValue(typeof(T), _ => new(fields)).Fields;
        }
    }

    private static class DiagnosticCache
    {
        internal static readonly ConditionalWeakTable<Type, DiagnosticMetadata> Fields = new();

        // Do not allocate even the cache until an explicit Fields call.
        static DiagnosticCache()
        {
        }
    }

    private sealed class DiagnosticMetadata(IReadOnlyList<ShaderTransferField> fields)
    {
        internal IReadOnlyList<ShaderTransferField> Fields { get; } = fields;
    }
}
