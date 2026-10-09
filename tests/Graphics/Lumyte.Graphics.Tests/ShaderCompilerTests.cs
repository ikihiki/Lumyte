using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Samples;
using Lumyte.Graphics.Shaders;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks compiler contracts on CI hosts without requiring a GPU.</summary>
public sealed class ShaderCompilerTests
{
    /// <summary>Checks online compilation for both device targets.</summary>
    /// <param name="target">The requested output target.</param>
    /// <returns>The completion of the compiler checks.</returns>
    [Theory]
    [InlineData(ShaderTarget.Wgsl)]
    [InlineData(ShaderTarget.SpirV)]
    public async Task CompilerProducesRequestedTargetAsync(ShaderTarget target)
    {
        IShaderCompiler compiler = new SlangShaderCompiler();
        using Stream source = typeof(ShaderExercise).Assembly.GetManifestResourceStream("Lumyte.Shaders.increment.slang")!;
        using var reader = new StreamReader(source);
        ShaderArtifact artifact = await compiler.CompileAsync(new ShaderCompilationDesc { Source = await reader.ReadToEndAsync(), Target = target });
        Assert.Equal(target, artifact.Target);
        Assert.Equal(ShaderStage.Compute, artifact.Stage);
        Assert.Equal("main", artifact.EntryPoint);
        Assert.NotEmpty(artifact.GetCode());
        Assert.Contains("entryPoints", artifact.ReflectionJson);
    }

    /// <summary>Checks failed Slang source returns diagnostics.</summary>
    /// <returns>The completion of the compiler checks.</returns>
    [Fact]
    public async Task InvalidSourceReturnsCompilerDiagnosticsAsync()
    {
        IShaderCompiler compiler = new SlangShaderCompiler();
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => compiler.CompileAsync(new ShaderCompilationDesc { Source = "this is not valid Slang;" }));
        Assert.Contains("Slang compilation failed", error.Message);
        Assert.Contains("error", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Checks cancellation before a compiler executable is started.</summary>
    /// <returns>The completion of the compiler checks.</returns>
    [Fact]
    public async Task CancellationPreventsCompilerStartAsync()
    {
        IShaderCompiler compiler = new SlangShaderCompiler("missing-slangc-executable");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => compiler.CompileAsync(new ShaderCompilationDesc { Source = "void main() {}" }, cancellation.Token));
    }
}
