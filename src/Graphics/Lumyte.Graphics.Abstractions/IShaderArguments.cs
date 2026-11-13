namespace Lumyte.Graphics.Abstractions;

/// <summary>Provides direct, AOT-safe serialization of application shader values.</summary>
public interface IShaderArguments
{
    /// <summary>Gets the logical root parameter name.</summary>
    static virtual string RootParameter => "arguments";

    /// <summary>Gets the logical shader structure name.</summary>
    static abstract string ShaderTypeName { get; }

    /// <summary>Gets the declared C# wire stride, or zero to infer it from explicit codec writes.</summary>
    static virtual int WireSizeInBytes => 0;

    /// <summary>Writes numeric members and opaque references without GPU transfer.</summary>
    /// <typeparam name="TWriter">The writer type, which can be a value type without boxing.</typeparam>
    /// <param name="writer">The snapshot destination.</param>
    void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter;

    /// <summary>Captures values through the type's implementation without codec registration.</summary>
    /// <typeparam name="T">The application value type.</typeparam>
    /// <param name="value">The application value.</param>
    /// <returns>The member snapshot.</returns>
    public static ShaderValueSnapshot Capture<T>(in T value)
        where T : struct, IShaderArguments
    {
        string root = T.RootParameter;
        string name = T.ShaderTypeName;
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        int size = T.WireSizeInBytes;
        var writer = new ShaderSnapshotWriter();
        value.Write(ref writer);
        if (size == 0)
        {
            size = typeof(IShaderData).IsAssignableFrom(typeof(T)) ? writer.SizeInBytes : checked((writer.SizeInBytes + 15) / 16 * 16);
        }

        if (size < writer.SizeInBytes)
        {
            throw new ArgumentException("The C# wire stride does not contain the codec writes.");
        }

        return new(root, name, writer.Values.AsReadOnly()) { SizeInBytes = size };
    }
}
