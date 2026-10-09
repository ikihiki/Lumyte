namespace Lumyte.Graphics.Abstractions;

/// <summary>Reads application members directly into a backend-independent value snapshot.</summary>
/// <typeparam name="T">The application value type.</typeparam>
/// <param name="value">The value to read.</param>
/// <param name="writer">The destination writer.</param>
public delegate void ShaderWriteAction<T>(in T value, IShaderValueWriter writer)
    where T : struct;
