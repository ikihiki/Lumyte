namespace Lumyte.Graphics.Abstractions;

/// <summary>Compiles Slang into backend-targeted code and reflection without owning a graphics device.</summary>
public interface IShaderCompiler
{
    /// <summary>Compiles one entry point; compilation diagnostics are returned by the failure exception.</summary>
    /// <param name="desc">The source and exact target requirements.</param>
    /// <param name="cancellationToken">Cancels compilation and cleans temporary output.</param>
    /// <returns>The compiled artifact and target-specific reflection.</returns>
    Task<ShaderArtifact> CompileAsync(ShaderCompilationDesc desc, CancellationToken cancellationToken = default);
}
