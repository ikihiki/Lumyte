namespace Lumyte.Graphics.Abstractions;

/// <summary>Selects command-owned root arguments without an intermediate numeric snapshot.</summary>
public interface IShaderRootEncoder
{
    /// <summary>Copies the complete wire value directly to a new native root allocation.</summary>
    /// <typeparam name="T">The application shader argument type.</typeparam>
    /// <param name="arguments">The value copied synchronously during selection.</param>
    /// <param name="scope">Optional preparation and execution admission constraints.</param>
    void SetRootArguments<T>(in T arguments, IShaderArgumentScope? scope = null)
        where T : IShaderArguments;

    /// <summary>Copies an unmanaged value directly to a new native root allocation.</summary>
    /// <typeparam name="T">The unmanaged wire type.</typeparam>
    /// <param name="arguments">The value copied synchronously during selection.</param>
    /// <param name="rootParameter">The logical root parameter name.</param>
    /// <param name="shaderTypeName">The shader type name, or the conventional C# type name.</param>
    /// <param name="scope">Optional preparation and execution admission constraints.</param>
    void SetRootArguments<T>(in T arguments, string rootParameter, string? shaderTypeName, IShaderArgumentScope? scope = null)
        where T : unmanaged;
}
