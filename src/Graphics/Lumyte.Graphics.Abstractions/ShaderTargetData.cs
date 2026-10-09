namespace Lumyte.Graphics.Abstractions;

/// <summary>Contains target-specific data extracted by a backend from a shader binary.</summary>
/// <param name="Target">The code target.</param>
/// <param name="Stage">The compiled stage.</param>
/// <param name="EntryPoint">The compiled entry.</param>
/// <param name="CompilerVersion">The actual compiler version.</param>
/// <param name="MatrixLayout">The compiler matrix layout.</param>
/// <param name="Code">An extracted copy of generated code.</param>
/// <param name="ReflectionJson">The corresponding reflection document.</param>
public sealed record ShaderTargetData(ShaderTarget Target, ShaderStage Stage, string EntryPoint, string CompilerVersion, string MatrixLayout, byte[] Code, string ReflectionJson);
