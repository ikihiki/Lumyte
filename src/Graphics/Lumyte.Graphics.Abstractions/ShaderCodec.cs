namespace Lumyte.Graphics.Abstractions;

/// <summary>Provides an AOT-safe registration point for generated application codecs.</summary>
/// <typeparam name="T">The application value type.</typeparam>
public static class ShaderCodec<T>
    where T : struct
{
    private static ShaderWriteAction<T>? _write;
    private static string? _root;
    private static string? _name;

    /// <summary>Registers a generated codec during application module initialization.</summary>
    /// <param name="rootParameter">The logical root parameter name.</param>
    /// <param name="shaderTypeName">The logical shader type name.</param>
    /// <param name="write">The generated direct member reader.</param>
    public static void Register(string rootParameter, string shaderTypeName, ShaderWriteAction<T> write)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootParameter);
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderTypeName);
        ArgumentNullException.ThrowIfNull(write);
        if (_write != null)
        {
            throw new InvalidOperationException("A shader codec is already registered for this type.");
        }

        (_root, _name, _write) = (rootParameter, shaderTypeName, write);
    }

    /// <summary>Captures values without reflection, GPU transfer or registration ownership.</summary>
    /// <param name="value">The application value.</param>
    /// <returns>The member snapshot.</returns>
    public static ShaderValueSnapshot Capture(in T value)
    {
        if (_write == null)
        {
            throw new NotSupportedException("No generated shader codec is registered for this type.");
        }

        var writer = new ShaderSnapshotWriter();
        _write(in value, writer);
        return new(_root!, _name!, writer.Values.AsReadOnly());
    }
}
