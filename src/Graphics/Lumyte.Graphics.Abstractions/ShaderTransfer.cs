using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lumyte.Graphics.Abstractions;

/// <summary>Copies ordinary unmanaged values without interfaces, generated code or conversion.</summary>
public static class ShaderTransfer
{
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

    /// <summary>Explicitly gets cached diagnostic C# field metadata in any configuration.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <returns>Diagnostic metadata only; it never determines transferred bytes.</returns>
    public static IReadOnlyList<ShaderTransferField> Fields<T>()
        where T : unmanaged
        => Fields(typeof(T));

    /// <summary>Explicitly gets cached diagnostic metadata for a shader argument struct.</summary>
    /// <typeparam name="T">The application shader argument type.</typeparam>
    /// <param name="value">The value identifying the diagnostic type; its contents are not inspected.</param>
    /// <returns>Static diagnostic metadata, without retaining or boxing the value.</returns>
    public static IReadOnlyList<ShaderTransferField> Fields<T>(in T value)
        where T : struct, IShaderArguments => ArgumentFields<T>();

    /// <summary>Explicitly gets cached CPU diagnostic field metadata for a value type; this never writes GPU data.</summary>
    /// <remarks>Type-only inspection requires dynamic code. On AOT, use generic argument inspection or captured argument snapshots for static metadata. Raw unmanaged reflection fallback is unavailable on AOT.</remarks>
    /// <param name="type">The C# value type to inspect.</param>
    /// <returns>Diagnostic metadata only; it never determines transferred bytes.</returns>
    public static IReadOnlyList<ShaderTransferField> Fields(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!type.IsValueType)
        {
            throw new ArgumentException("Type-based shader diagnostics require a value type.", nameof(type));
        }

        return DiagnosticCache.Fields.GetValue(type, static key => new DiagnosticMetadata(key)).Fields;
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

    private static void Describe(Type type, string prefix, int offset, List<ShaderTransferField> fields)
    {
        foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            string name = field.Name.StartsWith('<') ? field.Name[1..field.Name.IndexOf('>')] : field.Name;
            string path = prefix + name;
            var method = new DynamicMethod("ManagedFieldOffset", typeof(int), Type.EmptyTypes, typeof(ShaderTransfer).Module, true);
            ILGenerator il = method.GetILGenerator();
            LocalBuilder local = il.DeclareLocal(type);
            il.Emit(OpCodes.Ldloca, local);
            il.Emit(OpCodes.Ldflda, field);
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Ldloca, local);
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ret);
            int location = checked(offset + method.CreateDelegate<Func<int>>()());
            Type member = field.FieldType;
            if (member.IsPrimitive || member.IsEnum || member == typeof(Half) || member == typeof(Vector2) || member == typeof(Vector3) || member == typeof(Vector4) || member == typeof(Matrix4x4))
            {
                var size = new DynamicMethod("ManagedSize", typeof(int), Type.EmptyTypes, typeof(ShaderTransfer).Module, true);
                ILGenerator sizeIl = size.GetILGenerator();
                sizeIl.Emit(OpCodes.Sizeof, member);
                sizeIl.Emit(OpCodes.Ret);
                fields.Add(new(path, location, size.CreateDelegate<Func<int>>()(), member));
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
            IReadOnlyList<ShaderTransferField> fields = T.TransferFields;
            if (fields.Count != 0)
            {
                return fields;
            }

            // Raw outer offsets describe the wire layout only for unmanaged values.
            if (RuntimeFeature.IsDynamicCodeSupported && !RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                List<ShaderTransferField> reflected = [];
                Describe(typeof(T), string.Empty, 0, reflected);
                return reflected.AsReadOnly();
            }

            return fields;
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

    private sealed class DiagnosticMetadata(Type type)
    {
        internal IReadOnlyList<ShaderTransferField> Fields { get; } = Create(type);

        private static IReadOnlyList<ShaderTransferField> Create(Type type)
        {
            if (!RuntimeFeature.IsDynamicCodeSupported)
            {
                return [];
            }

            if (typeof(IShaderArguments).IsAssignableFrom(type))
            {
                MethodInfo method = typeof(ShaderTransfer).GetMethod(nameof(ArgumentFields), BindingFlags.Static | BindingFlags.NonPublic)!;
                return (IReadOnlyList<ShaderTransferField>)method.MakeGenericMethod(type).Invoke(null, null)!;
            }

            List<ShaderTransferField> fields = [];
            Describe(type, string.Empty, 0, fields);
            return fields.AsReadOnly();
        }
    }
}
