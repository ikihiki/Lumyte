namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes one Slang source and entry point without starting compilation.</summary>
public sealed record ShaderCompilationDesc
{
    /// <summary>Gets the Slang source text.</summary>
    public required string Source { get; init; }

    /// <summary>Gets the shader entry point name.</summary>
    public string EntryPoint { get; init; } = "main";

    /// <summary>Gets the entry point stage.</summary>
    public ShaderStage Stage { get; init; } = ShaderStage.Compute;

    /// <summary>Gets the required output code format.</summary>
    public ShaderTarget Target { get; init; } = ShaderTarget.Wgsl;
}
