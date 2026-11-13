namespace Lumyte.Graphics.Abstractions;

/// <summary>Provides an AOT-safe bulk transfer of C#-authoritative shader wire bytes.</summary>
public interface IShaderArguments
{
    /// <summary>Gets the logical root parameter name.</summary>
    static virtual string RootParameter => "arguments";

    /// <summary>Gets the logical shader structure name.</summary>
    static abstract string ShaderTypeName { get; }

    /// <summary>Gets the C# wire size, independent of shader reflection and instances.</summary>
    static abstract int TransferSize { get; }

    /// <summary>Gets C# field locations for explicit generic diagnostics; capture never calls or retains this getter.</summary>
    static virtual IReadOnlyList<ShaderTransferField> TransferFields => [];

    /// <summary>Explicitly gets diagnostic field metadata for a reference-type provider.</summary>
    /// <returns>The C# wire fields, or an empty list when no diagnostic provider is implemented.</returns>
    /// <remarks>Capture retains reference-type providers without invoking this method. Value types use type-based diagnostics without boxing.</remarks>
    IReadOnlyList<ShaderTransferField> GetTransferFields() => [];

    /// <summary>Copies one complete unmanaged wire value and records opaque reference fixups.</summary>
    /// <typeparam name="TWriter">The writer type, which can be a value type without boxing.</typeparam>
    /// <param name="writer">The snapshot destination.</param>
    void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter;

    /// <summary>Captures values through the type's implementation without codec registration.</summary>
    /// <typeparam name="T">The application transfer type.</typeparam>
    /// <param name="value">The application value.</param>
    /// <returns>The immutable wire snapshot.</returns>
    public static ShaderValueSnapshot Capture<T>(in T value)
        where T : IShaderArguments
    {
        ArgumentNullException.ThrowIfNull(value);
        string root = T.RootParameter;
        string name = T.ShaderTypeName;
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ShaderSnapshotWriter writer = default;
        value.Write(ref writer);
        if (writer.Data == null || writer.Data.Length != T.TransferSize)
        {
            throw new InvalidOperationException("The bulk transfer must match the static C# wire size.");
        }

        foreach (ShaderValue reference in writer.Values ?? [])
        {
            if (reference.Offset < 0 || reference.Offset > writer.Data.Length - 16)
            {
                throw new ArgumentException("A reference slot is outside the C# wire value.");
            }
        }

        return new(root, name, writer.Data, writer.Values?.AsReadOnly() ?? (IReadOnlyList<ShaderValue>)[], [])
        {
            DiagnosticType = typeof(T).IsValueType ? typeof(T) : null,
            DiagnosticSource = typeof(T).IsValueType ? null : value,
        };
    }
}
