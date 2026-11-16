using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shaders.Benchmark;

/// <summary>Public compiler and artifact processing; no GPU module is created.</summary>
[MemoryDiagnoser]
public class ShaderCompilerBenchmarks
{
    private const string Source = """[shader("compute")] [numthreads(1,1,1)] void main(uint3 id : SV_DispatchThreadID) {}""";
    private readonly SlangShaderCompiler _compiler = new();
    private byte[] _artifact = null!;

    /// <summary>Compiles the immutable transport fixture outside artifact parsing.</summary>
    /// <returns>The setup completion.</returns>
    [GlobalSetup]
    public async Task SetupAsync() => _artifact = (await _compiler.CompileAsync(new ShaderCompilationDesc { Source = Source, Target = ShaderTarget.SpirV })).GetBinary();

    /// <summary>Invokes the actual external Slang compiler, diagnostic only.</summary>
    /// <returns>The compiled artifact.</returns>
    [Benchmark]
    [BenchmarkCategory("Diagnostic")]
    public Task<ShaderArtifact> CompileSpirVAsync() => _compiler.CompileAsync(new ShaderCompilationDesc { Source = Source, Target = ShaderTarget.SpirV });

    /// <summary>Validates and unpacks the compiled CPU transport artifact.</summary>
    /// <returns>The actual target payload.</returns>
    [Benchmark]
    [BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
    public ShaderTargetData ReadArtifact() => new ShaderArtifact(_artifact).GetTarget(ShaderTarget.SpirV);
}
