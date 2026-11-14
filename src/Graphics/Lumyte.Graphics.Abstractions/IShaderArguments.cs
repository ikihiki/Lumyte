namespace Lumyte.Graphics.Abstractions;

/// <summary>Provides an AOT-safe bulk transfer of C#-authoritative shader wire bytes from argument structs.</summary>
public interface IShaderArguments
{
    /// <summary>Gets the logical root parameter name.</summary>
    static virtual string RootParameter => "arguments";

    /// <summary>Gets the logical shader structure name.</summary>
    static abstract string ShaderTypeName { get; }

    /// <summary>Gets the C# wire size, independent of shader reflection and instances.</summary>
    static abstract int TransferSize { get; }

    /// <summary>Gets C# field locations for explicit generic diagnostics; capture defers this getter through a static provider without retaining the value.</summary>
    static virtual IReadOnlyList<ShaderTransferField> TransferFields => [];

    /// <summary>Copies one complete unmanaged wire value and records opaque reference fixups.</summary>
    /// <typeparam name="TWriter">The writer type, which can be a value type or ref struct without boxing.</typeparam>
    /// <param name="writer">The snapshot destination.</param>
    void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter, allows ref struct;

    /// <summary>Captures values through the type's implementation without codec registration.</summary>
    /// <typeparam name="T">The application transfer struct.</typeparam>
    /// <param name="value">The application value.</param>
    /// <returns>The immutable wire snapshot.</returns>
    public static ShaderValueSnapshot Capture<T>(in T value)
        where T : struct, IShaderArguments
    {
        string root = T.RootParameter;
        string name = T.ShaderTypeName;
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ShaderSnapshotWriter writer = new(T.TransferSize);
        value.Write(ref writer);
        writer.Complete();

        return new(root, name, writer.Data, writer.Values?.AsReadOnly() ?? (IReadOnlyList<ShaderValue>)[], [])
        {
            DiagnosticType = typeof(T),
            DiagnosticFields = ShaderTransfer.ArgumentFields<T>,
        };
    }
}
